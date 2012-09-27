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

		public static void Initialize(DocumentDatabase db)
		{
			
			SettingsCache = new UpdateCascadeSettingsCache();
			SettingsCache.EnsureInitialized(db);
			RunningOperationsCoordinator = new UpdateCascadeRunningOperationsCoordinator(db);
		}

		public static bool IsShutDownInProgress { get; set; }		
	}
}
