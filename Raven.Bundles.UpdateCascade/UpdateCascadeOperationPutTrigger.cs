using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Database.Plugins;
using Raven.Json.Linq;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using NLog;

namespace Raven.Bundles.UpdateCascade
{
	public class UpdateCascadeOperationPutTrigger : AbstractPutTrigger
	{
		private static readonly Logger log = LogManager.GetCurrentClassLogger();

		Repository<UpdateCascadeOperation> operationRepository;

		Services services;

		public override void Initialize()
		{
			base.Initialize();
			services = Services.GetServices(this.Database);
			operationRepository = new Repository<UpdateCascadeOperation>(this.Database);
		}

		public override void AfterCommit(string key, RavenJObject document, RavenJObject metadata, Guid etag)
		{
			base.AfterCommit(key, document, metadata, etag);
			var entityName = metadata.Value<string>(Constants.RavenEntityName);
			if (entityName != UpdateCascadeOperation.EntityName) return;
			if (document.Value<string>("Status") != "Pending") return;
			log.Trace("A new operation with id {0} has been put", key);
			var operation = document.JsonDeserialization<UpdateCascadeOperation>();
			var referencedDocId = document.Value<string>("ReferencedDocId");
			var referencedDoc = this.Database.Get(referencedDocId, null);

			if (referencedDoc == null) return;

			if (services.RunningOperationsCoordinator != null)
			{
				services.RunningOperationsCoordinator.TryStartOperation(operation, referencedDoc);
			}

		}
	}
}
