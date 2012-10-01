using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Database;
using System.Collections.Concurrent;

namespace Raven.Bundles.UpdateCascade
{
	internal class Services
	{

		private static ConcurrentDictionary<DocumentDatabase, Services> servicesEntries = new ConcurrentDictionary<DocumentDatabase, Services>();

		public static Services GetServices(DocumentDatabase db)
		{
			var services = servicesEntries.GetOrAdd(db, x => new Services(x));
			services.EnsureInitialized();
			return services;
		}

		public UpdateCascadeRunningOperationsCoordinator RunningOperationsCoordinator { get; private set; }

		public UpdateCascadeSettingsCache SettingsCache { get; private set; }

		private volatile bool isInitialized;

		private object initializeLockObject = new object();

		private readonly DocumentDatabase db;

		private Services(DocumentDatabase db)
		{
			this.db = db;
		}

		private void EnsureInitialized()
		{
			if (!isInitialized)
			{
				lock (initializeLockObject)
				{
					if (!isInitialized)
					{
						Initialize();
					}
				}
			}
		}

		private void Initialize()
		{			
			SettingsCache = new UpdateCascadeSettingsCache();
			SettingsCache.EnsureInitialized(db);
			RunningOperationsCoordinator = new UpdateCascadeRunningOperationsCoordinator(db);
		}

		public bool IsShutDownInProgress { get; set; }	
	}
}
