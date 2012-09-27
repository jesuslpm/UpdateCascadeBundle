using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using Raven.Database.Plugins;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Data;

namespace Raven.Bundles.UpdateCascade
{
	public class UpdateCascadeSettingPutTrigger : AbstractPutTrigger
	{

		public override void AfterCommit(string key, Json.Linq.RavenJObject document, Json.Linq.RavenJObject metadata, Guid etag)
		{
			base.AfterCommit(key, document, metadata, etag);
			var entityName = metadata.Value<string>(Constants.RavenEntityName);
			if (entityName != UpdateCascadeSetting.EntityName) return;			
			Services.SettingsCache.InvalidateCacheItem(key);
		}
	}

	public class UpdateCascadeSettingDeleteTrigger : AbstractDeleteTrigger
	{
		public override void AfterCommit(string key)
		{
			
			base.AfterCommit(key);
			if (key.StartsWith(UpdateCascadeSetting.IdPrefix))
			{
				Services.SettingsCache.InvalidateCacheItem(key);
			}
		}
	}
}
