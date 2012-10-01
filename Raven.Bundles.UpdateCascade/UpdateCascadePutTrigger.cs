using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Database.Plugins;
using Raven.Json.Linq;
using Raven.Abstractions.Data;
using System.Threading;

namespace Raven.Bundles.UpdateCascade
{
	public class UpdateCascadePutTrigger : AbstractPutTrigger
	{
		private Repository<UpdateCascadeOperation> operationRepository;
		private ThreadLocal<JsonDocument> originalDocument = new ThreadLocal<JsonDocument>();
		private Services services;


		public override void Initialize()
		{
			base.Initialize();
			operationRepository = new Repository<UpdateCascadeOperation>(this.Database);
			services = Services.GetServices(this.Database);
		}

		public override void OnPut(string key, RavenJObject document, RavenJObject metadata, TransactionInformation transactionInformation)
		{
			base.OnPut(key, document, metadata, transactionInformation);			
			if (GetSetting(metadata) == null)
			{
				originalDocument.Value = null;
			}
			else
			{
				originalDocument.Value = Database.Get(key, transactionInformation);
			}
		}

		private UpdateCascadeSetting GetSetting(RavenJObject metadata)
		{
			var entityName = metadata.Value<string>(Constants.RavenEntityName);
			UpdateCascadeSetting setting = null;
			string settingId = UpdateCascadeSetting.GetId(entityName);
			services.SettingsCache.EnsureInitialized(this.Database);
			services.SettingsCache.TryGetValue(settingId, out setting);
			return setting;
		}

		public override void AfterPut(string key, RavenJObject document, RavenJObject metadata, Guid etag, TransactionInformation transactionInformation)
		{
			base.AfterPut(key, document, metadata, etag, transactionInformation);
			JsonDocument originalDocument = this.originalDocument.Value;
			if (originalDocument == null) return;
			var setting = GetSetting(metadata);
			if (setting == null) return;
			if (HasAnyReferencedPropertyChanged(originalDocument.DataAsJson, document, setting))
			{
				var operation = new UpdateCascadeOperation(setting, key, etag);
				operationRepository.Save(operation, transactionInformation);
			}
		}

		private static bool HasAnyReferencedPropertyChanged(RavenJObject original, RavenJObject current, UpdateCascadeSetting setting)
		{
			return setting.DenormalizedReferencePropertyNames.Any(pn => !RavenJToken.DeepEquals(original[pn], current[pn]));
		}		
	}
}
