using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Database.Plugins;
using Raven.Database;

namespace Raven.Bundles.UpdateCascade
{
	public sealed class UpdateCascadeStartupTask : IStartupTask, IDisposable
	{

		private DocumentDatabase db;

		#region IStartupTask Members

		public void Execute(Database.DocumentDatabase database)
		{
			db = database;
			Services.Initialize(database);
			this.db.PutIndex(UpdateCascadeOperation.ByStatusIndexName, UpdateCascadeOperation.GetByStatusIndexDefinition());
			Services.RunningOperationsCoordinator.RestartNotCompletedOperations();
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
				Services.RunningOperationsCoordinator.CancelAllOperations();
			}
		}

		#endregion
	}
}
