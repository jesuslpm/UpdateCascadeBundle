using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Database;
using Raven.Json.Linq;
using Raven.Abstractions.Data;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Util;
using System.Globalization;
using NLog;

namespace Raven.Bundles.UpdateCascade
{
	internal class UpdateCascadeOperationExecutor
	{
		private DocumentDatabase db;
		private UpdateCascadeSetting setting;
		private UpdateCascadeOperation operation;
		private JsonDocument referencedDoc;
		private Repository<UpdateCascadeOperation> operationRepository;

		private static readonly Logger log = LogManager.GetCurrentClassLogger();

		public UpdateCascadeOperationExecutor(DocumentDatabase db, UpdateCascadeSetting setting, UpdateCascadeOperation operation, JsonDocument referencedDoc)
		{
			this.db = db;
			this.setting = setting;
			this.operation = operation;
			this.referencedDoc = referencedDoc;
		}

		private Task currentTask;

		private int _isRunning;
		public bool IsRunning
		{
			get
			{
				return _isRunning != 0;
			}
		}

		CancellationToken cancellationToken;

		public Task ExecuteAsync(CancellationToken cancellationToken)
		{
			if (Interlocked.CompareExchange(ref _isRunning, -1, 0) != 0)
			{
				log.Error("Called ExecuteAsync for the update cascade operation {0} which is already running", this.operation.Id);
				throw new InvalidOperationException("The operation is already running");
			}
			this.cancellationToken = cancellationToken;
			currentTask = Task.Factory.StartNew(delegate 
			{
				try
				{
					Execute();
				}
				catch (OperationCanceledException)
				{					
					throw;
				}
				catch (Exception ex)
				{
					log.FatalException(string.Format("Error executing update cascade operation {0}", this.operation.Id), ex);
				}				
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
			return currentTask;
		}

		private void SaveOperation()
		{
			this.operationRepository.Save(operation, null);
			log.Debug("Update cascade operation {0} has been saved", this.operation.Id);
		}

		private void SaveOperationSilentlyIgnoringError()
		{
			try
			{
				SaveOperation();
			}
			catch (Exception ex)
			{
				log.ErrorException(string.Format("Cannot save update cascade operation {0}", this.operation.Id), ex) ;
			}
		}

		private void Execute()
		{
			this.operation.Status = UpdateCascadeOperationStatus.Executing;
			operation.StartedDate = DateTime.UtcNow;
			operationRepository = new Repository<UpdateCascadeOperation>(db);
			var completedReferencingEntityNames = this.operation.CollectionOperations
				.Where( x => x.IsCompleted)
				.Select( x => x.ReferencingEntityName)
				.ToHashSet();

			var notCompletedReferencingCollections = this.setting.ReferencingCollections.Values
				.Where( rc => ! completedReferencingEntityNames.Contains( rc.ReferencingEntityName));

			try
			{
				log.Trace("Update cascade operation {0} started", operation.Id);				
				foreach (var referencingCollectionSetting in notCompletedReferencingCollections)
				{
					var collectionOperation = this.operation.CollectionOperations.FirstOrDefault(co => co.ReferencingEntityName == referencingCollectionSetting.ReferencingEntityName);
					if (collectionOperation == null)
					{
						collectionOperation = new ReferencingCollectionOperation
						{
							ReferencingEntityName = referencingCollectionSetting.ReferencingEntityName
						};
						this.operation.CollectionOperations.Add(collectionOperation);
					}
					
					UpdateCascadeReferencingCollection(referencingCollectionSetting, collectionOperation);
				}
				
				operation.Status = operation.CollectionOperations.Any(co => co.Status == UpdateCascadeOperationStatus.Failed)
					? UpdateCascadeOperationStatus.Failed : UpdateCascadeOperationStatus.CompletedSuccessfully;
				log.Trace("Update cascade operation {0} completed with status {1}", operation.Status);
			}
			catch (OperationCanceledException)
			{
				operation.Status = UpdateCascadeOperationStatus.Canceled;
				log.Trace("Update cascade operation {0} has been canceled", this.operation.Id);
				throw;
			}
			catch (Exception)
			{
				operation.Status = UpdateCascadeOperationStatus.Failed;
				throw;
			}
			finally
			{
				operation.CompletedDate = DateTime.UtcNow;
				if (! (Services.IsShutDownInProgress && operation.Status == UpdateCascadeOperationStatus.Canceled))
				{
					SaveOperationSilentlyIgnoringError();
				}
				operationRepository = null;
			}			
		}

		private void UpdateCascadeReferencingCollection(ReferencingCollectionSetting rc, ReferencingCollectionOperation co)
		{
			co.StartedDate = DateTime.UtcNow;
			co.Status = UpdateCascadeOperationStatus.Executing;
			co.UpdatedDocumentCount = 0;
			SaveOperationSilentlyIgnoringError(); // we can continue anyway
			log.Trace("Update cascade {0} referencing document collection for update cascade operation {1} started", rc.ReferencingEntityName, operation.Id);
			try
			{
				int updatedByIndex = 0;
				Guid lastEtag;
				do
				{
					lastEtag = db.GetLastEtag();
					var query = new IndexQuery
					{
						CutoffEtag = lastEtag,
						Query = string.Format("{0}:{1} AND {2}:{3}NULL TO {4}{5}", rc.ReferencedIdPropertyNameInIndex, RavenQuery.Escape(referencedDoc.Key),
							rc.ReferencedEtagPropertyNameInIndex, "{", RavenQuery.Escape(Convert.ToString(referencedDoc.Etag, CultureInfo.InvariantCulture)), "}")

					};
					updatedByIndex = db.UpdateByIndex(rc.IndexName, query, doc => UpdateDocumentWithProgressReport(doc, co),
						TimeSpan.FromHours(8), cancellationToken, null);

				} while (updatedByIndex > 0);

				db.UpdateDocumentsAfter(lastEtag, UpdateDocument, cancellationToken, null);
				co.Status = UpdateCascadeOperationStatus.CompletedSuccessfully;
				log.Trace("Update cascade {0} documents for operation {1} completed successfully. {2} documents have been updated in {3}", rc.ReferencingEntityName, operation.Id, co.UpdatedDocumentCount, co.ElapsedTime);
			}
			catch (OperationCanceledException) // save the operation and rethrow
			{
				co.Status = UpdateCascadeOperationStatus.Canceled;
				throw;
			}
			catch (Exception ex) // Log the error, save the operation, and move to the next.
			{				
				co.ErrorMessage = ex.Message;
				co.Status = UpdateCascadeOperationStatus.Failed;
				log.ErrorException(string.Format("Update cascade {0} documents for operation {1} failed miserably. Moving on the next one ...", rc.ReferencingEntityName, operation.Id), ex);
			}
			finally
			{
				co.CompletedDate = DateTime.UtcNow;
				if (!(Services.IsShutDownInProgress && co.Status == UpdateCascadeOperationStatus.Canceled))
					SaveOperationSilentlyIgnoringError(); // we want to preserve OperationCanceledException.
			}
		}


		private bool UpdateDocumentWithProgressReport(JsonDocument referencingDocument, ReferencingCollectionOperation collectionOperation)
		{
			if (UpdateDocument(referencingDocument))
			{
				collectionOperation.UpdatedDocumentCount++;
				if (collectionOperation.UpdatedDocumentCount % 200 == 0)
				{
					log.Trace("Updating progress for operation {0}. {1} {2} documents have been updated so far", operation.Id, collectionOperation.UpdatedDocumentCount, collectionOperation.ReferencingEntityName);
					SaveOperationSilentlyIgnoringError(); // updating progress is not so important
				}
				return true;
			}
			else
			{
				return false;
			}
		}

		private bool UpdateDocument(JsonDocument referencingDocument)
		{
			var referencingEntityName = referencingDocument.Metadata.Value<string>(Constants.RavenEntityName);
			ReferencingCollectionSetting referencingCollectionSetting = null;
			if (!this.setting.ReferencingCollections.TryGetValue(referencingEntityName, out referencingCollectionSetting))
			{
				log.Debug("{0} document doesn't need to be cascade updated because it doesn't belong to any document collection that hold denormalized references to {1}. Operation {2} ", referencingDocument.Key, setting.ReferencedEntityName, operation.Id);
				return false;
			}

			var denormalizedReferences = referencingDocument.DataAsJson.GetObjectsAtPath(referencingCollectionSetting.ReferencingPropertyPath);
			bool shouldUpdate = false;
			foreach (var reference in denormalizedReferences)
			{
				Guid? referencedEtag = reference.Value<Guid?>("Etag");
				var referencedDocId = reference.Value<string>("Id");

				if (referencedDocId == referencedDoc.Key && (referencedEtag == null || 
					Buffers.Compare(referencedEtag.Value.ToByteArray(), referencedDoc.Etag.Value.ToByteArray()) < 0))
				{
					shouldUpdate = true;
					foreach (var property in setting.DenormalizedReferencePropertyNames)
					{
						reference[property] = referencedDoc.DataAsJson[property].CloneToken();
					}
					reference["Etag"] = RavenJToken.FromObject(referencedDoc.Etag.Value);
				}
			}
			if (shouldUpdate)
			{
				log.Debug("{0} document has been cascade updated in memory beacause it references {1} document and its referencing Etag is prior to the referenced document one {2}", referencingDocument.Key, referencedDoc.Key, referencedDoc.Etag);
			}
			else
			{
				log.Debug("{0} document has not been cascade updated in memory beacause it does not references {1} document or its referencing Etag is subsequent to the referenced document one {2}", referencingDocument.Key, referencedDoc.Key, referencedDoc.Etag);
			}
			return shouldUpdate;
		}
	}
}
