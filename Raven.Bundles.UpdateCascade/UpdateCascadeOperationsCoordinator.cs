using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Raven.Abstractions.Extensions;
using Raven.Database;
using NLog;
using Raven.Abstractions.Data;

namespace Raven.Bundles.UpdateCascade
{
	internal class UpdateCascadeRunningOperationsCoordinator
	{
		private DocumentDatabase db;
		private static readonly Logger log = LogManager.GetCurrentClassLogger();
		private Repository<UpdateCascadeOperation> repository;

		private class RunningOperation
		{
			public CancellationTokenSource TokenSource;
			public UpdateCascadeOperation Operation;
			public UpdateCascadeOperationExecutor Executor;
			public Task ExecutorTask;
		}

		public UpdateCascadeRunningOperationsCoordinator(DocumentDatabase db)
		{
			this.db = db;
			this.repository = new Repository<UpdateCascadeOperation>(db);
		}


		private Dictionary<string, RunningOperation> runningOperations = new Dictionary<string, RunningOperation>();


		public void RestartNotCompletedOperations()
		{
			var lastEtag = db.GetLastEtag();
			db.WaitForIndexNotStale(UpdateCascadeOperation.ByStatusIndexName, null, lastEtag, TimeSpan.FromHours(1), CancellationToken.None);
			var indexQuery = new IndexQuery
			{
				CutoffEtag = lastEtag,
				PageSize = 1024,
				Query = "Status:Pending OR Status:Executing"
			};
			IList<string> docIds = null;
			int restartedOperations = 0;
			do
			{
				docIds = db.QueryDocumentIds(UpdateCascadeOperation.ByStatusIndexName, indexQuery, CancellationToken.None);
				indexQuery.Start += docIds.Count;
				db.TransactionalStorage.Batch(_ =>
				{
					foreach (var id in docIds)
					{
						var operation = repository.Get(id);
						JsonDocument referenceDocument = null;
						if (operation != null) referenceDocument = db.Get(operation.ReferencedDocId, null);
						
						if (operation != null && referenceDocument != null && TryStartOperation(operation, referenceDocument))
						{
							restartedOperations++;
							log.Debug("Operation {0} has been restarted after a reboot. {1} operations restarted so far", id, restartedOperations);
						}
					}
				});

			} while (docIds.Count == indexQuery.PageSize);
		}

		public void CancelAllOperations()
		{
			Task[] tasks = null;
			int taskIndex = 0;
			lock (this.runningOperations)
			{
				tasks = new Task[this.runningOperations.Count];
				foreach (var kv in runningOperations)
				{										
					if (!kv.Value.TokenSource.IsCancellationRequested)
					{
						kv.Value.TokenSource.Cancel();
					}
					tasks[taskIndex] = kv.Value.ExecutorTask;
					taskIndex++;
				}
			}
			// I don't think waiting for the tasks was needed
			//try
			//{
			//    Task.WaitAll(tasks);
			//}
			//catch (AggregateException ex)
			//{
			//    ex.Handle(x => x is TaskCanceledException);
			//}
		}

		public bool TryStartOperation(UpdateCascadeOperation operation, JsonDocument referencedDoc)
		{
			if (Services.IsShutDownInProgress)
			{
				log.Warn("Tried to start operation {0} while shuting down", operation.Id);
				return false;
			}

			RunningOperation co = null;
			UpdateCascadeSetting setting;
			if (!Services.SettingsCache.TryGetValue(operation.UpdateCascadeSettingId, out setting))
			{
				log.Error("Tried to add and run the operation {0}. But there is no corresponding setting {1}", operation.Id, operation.UpdateCascadeSettingId);
				return false;
			}
			
			lock (runningOperations)
			{
				if (runningOperations.TryGetValue(operation.ReferencedDocId, out co))
				{
					// the operation might be already here. This shouldn't occur
					if (operation.Id == co.Operation.Id) 
					{
						log.Warn("Tried to start an operation that is already started. Operation Id = {0}", operation.Id);
						return false;
					}
					// the operation might refer to an older entity. This is unprobable, I think
					if (Buffers.Compare(operation.ReferencedDocEtag.ToByteArray(), co.Operation.ReferencedDocEtag.ToByteArray()) < 0) 
					{
						log.Warn("Tried to start an operation that refers to an entity which is older than the referenced by a running operation. Older operation id: {0}, existing operation id: {1}", operation.Id, co.Operation.Id);
						return false;
					}

					log.Warn("The same referenced entity {0} has been updated while a previous update cascade operation of that entity is in progress, that might indicate that the document is updated so often that referencing entities cannot be updated at time. Update cascade bundle is not recomended in this scenario", operation.ReferencedDocId);

					// the same referenced entity has been updated while a previous update cascade operation of that entity is in progress
					// we need to cancel that operation and span a new one.
					var tokenSource = co.TokenSource;
					if (tokenSource != null) tokenSource.Cancel();
					try
					{
						var task = co.ExecutorTask;
						if (task!= null) task.Wait();
					}
					catch (AggregateException ex) 
					{ 
						ex.Handle(x => x is TaskCanceledException); 
					}
				}
				var runningOperation = new RunningOperation
				{
					Operation = operation,
					TokenSource =  new CancellationTokenSource(),
					Executor = new UpdateCascadeOperationExecutor(db, setting, operation, referencedDoc),				
				};
				runningOperations[operation.ReferencedDocId] = runningOperation;
				log.Trace("Starting operation: {0}", operation.Id);
				runningOperation.ExecutorTask = runningOperation.Executor.ExecuteAsync(runningOperation.TokenSource.Token);				
				runningOperation.ExecutorTask.ContinueWith(t =>
				{
					if (!Services.IsShutDownInProgress)
					{
						lock (runningOperations)
						{
							RunningOperation ro;
							if (runningOperations.TryGetValue(operation.ReferencedDocId, out ro))
							{
								if (ro.Operation.Id == operation.Id)
								{
									runningOperations.Remove(operation.ReferencedDocId);
								}
							}
							t.Dispose();
						}
					}

				}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

				return true;
			}
		}
	}
}
