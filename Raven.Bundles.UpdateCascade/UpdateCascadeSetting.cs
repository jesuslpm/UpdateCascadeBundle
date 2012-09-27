using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Imports.Newtonsoft.Json;

namespace Raven.Bundles.UpdateCascade
{

	public class ReferencingCollectionSetting
	{
		public string ReferencingEntityName { get; set; }
		public string ReferencingPropertyPath { get; set; }
		public string IndexName { get; set; }
		public string ReferencedIdPropertyNameInIndex { get; set; }
		public string ReferencedEtagPropertyNameInIndex { get; set; }
	}

	public class UpdateCascadeSetting : IEntity
	{
		public const string IdPrefix = "Raven/UpdateCascadeSettings/";
		public const string EntityName = "UpdateCascadeSettings";
		public const int MaxAllowedCascadeSettings = 1024;

		public static string GetId(string referencedEntityName)
		{
			return IdPrefix + referencedEntityName;
		}


		/// <summary>
		/// Raven/UpdateCascadeSettings/The Raven-Entity-Name of documents to watch for changes and then apply update cascade operations to other documents.
		/// </summary>
		[JsonIgnore]
		public string Id
		{
			get
			{
				return GetId(ReferencedEntityName);
			}
		}

		[JsonIgnore]
		public Guid? Etag { get; set; }

		/// <summary>
		/// The Raven-Entity-Name of documents to watch for changes and then apply update cascade operations to other documents.
		/// </summary>
		public string ReferencedEntityName { get; set; }

		/// <summary>
		/// The document collections that hold denormalized references to ReferencedEntityName entities, including the update cascade setting for each collection
		/// </summary>
		public Dictionary<string, ReferencingCollectionSetting> ReferencingCollections { get; set; }

		/// <summary>
		/// The property names of the denormalized reference, excluding id and ETag.
		/// Theses properties must correspond to properties of the referenced entity.
		/// Having the same name and type.
		/// </summary>
		public List<string> DenormalizedReferencePropertyNames { get; set; }

		public UpdateCascadeSetting()
		{
			this.ReferencingCollections = new Dictionary<string,ReferencingCollectionSetting>();
			this.DenormalizedReferencePropertyNames = new List<string>();
		}
	}
}
