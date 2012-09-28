using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Database;

namespace Raven.Bundles.UpdateCascade
{
	internal static class Services
	{

		public static UpdateCascadeRunningOperationsCoordinator RunningOperationsCoordinator { get; private set; }

		public static UpdateCascadeSettingsCache SettingsCache { get; private set; }

		private static volatile bool isInitialized;

		private static object initializeLockObject = new object();

		public static void EnsureInitialized(DocumentDatabase db)
		{
			if (!isInitialized)
			{
				lock (initializeLockObject)
				{
					if (!isInitialized)
					{
						Initialize(db);
					}
				}
			}
		}

		private static void Initialize(DocumentDatabase db)
		{			
			SettingsCache = new UpdateCascadeSettingsCache();
			SettingsCache.EnsureInitialized(db);
			RunningOperationsCoordinator = new UpdateCascadeRunningOperationsCoordinator(db);
		}

		public static bool IsShutDownInProgress { get; set; }		
	}
}
