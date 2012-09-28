using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Database.Plugins;
using Raven.Database;
using NLog;
using System.Threading.Tasks;

namespace Raven.Bundles.UpdateCascade
{
	public sealed class UpdateCascadeStartupTask : IStartupTask, IDisposable
	{

		private static readonly Logger log = LogManager.GetCurrentClassLogger();

		private DocumentDatabase db;

		#region IStartupTask Members

		public void Execute(Database.DocumentDatabase database)
		{
			try
			{
				Services.IsShutDownInProgress = false;
				db = database;
				this.db.PutIndex(UpdateCascadeOperation.ByStatusIndexName, UpdateCascadeOperation.GetByStatusIndexDefinition());
				Services.EnsureInitialized(database);				
				Task.Factory.StartNew(Services.RunningOperationsCoordinator.RestartNotCompletedOperations).ContinueWith(t =>
				{
					if (t.Status == TaskStatus.Faulted && t.Exception != null)
					{
						log.ErrorException("Failed to restart not completed operations", t.Exception);
					}
				});
			}
			catch (Exception ex)
			{
				log.FatalException("Failed to execute UpdateCascadeStartupTask", ex);
			}
		}

		#endregion


		#region IDisposable Members

		private bool isDisposed;

		public void Dispose()
		{
			if (!isDisposed)
			{
				isDisposed = true;
				Services.IsShutDownInProgress = true;
				if (Services.RunningOperationsCoordinator != null)
				{
					Services.RunningOperationsCoordinator.CancelAllOperations();
				}
			}
		}

		#endregion
	}
}
