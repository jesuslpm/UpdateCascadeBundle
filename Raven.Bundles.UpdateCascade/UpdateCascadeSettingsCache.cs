using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using Raven.Database;

namespace Raven.Bundles.UpdateCascade
{
	internal class UpdateCascadeSettingsCache
	{
		
		private Repository<UpdateCascadeSetting> repository;
		private ConcurrentDictionary<string, UpdateCascadeSetting> cache;

		private volatile bool isInitialized;

		public void Initialize(DocumentDatabase db)
		{
			repository = new Repository<UpdateCascadeSetting>(db);

			var settings = repository.GetWithIdStartingWith(UpdateCascadeSetting.IdPrefix, 0,  UpdateCascadeSetting.MaxAllowedCascadeSettings);
			var keyValuePairs = settings.Select(x => new KeyValuePair<string, UpdateCascadeSetting>(x.Id, x));
			cache = new ConcurrentDictionary<string, UpdateCascadeSetting>(1, keyValuePairs, StringComparer.InvariantCulture);
			isInitialized = true;
		}


		private object initializeLockObject = new object();
		public void EnsureInitialized(DocumentDatabase db)
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

		public bool TryGetValue(string id, out UpdateCascadeSetting setting)
		{
			return cache.TryGetValue(id, out setting);
		}

		public void InvalidateCacheItem(string id)
		{
			var setting = repository.Get(id);
			if (setting == null) cache.TryRemove(id, out setting);
			else cache.AddOrUpdate(id, setting, (key, existing) => setting);
		}
	}
}
