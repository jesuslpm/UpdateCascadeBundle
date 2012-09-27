using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Imports.Newtonsoft.Json;
using Raven.Database;
using Raven.Imports.Newtonsoft.Json.Linq;
using Raven.Json.Linq;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Indexing;

namespace Raven.Bundles.UpdateCascade
{
	public enum UpdateCascadeOperationStatus
	{
		Pending,
		Executing,
		Failed,
		CompletedSuccessfully,
		Canceled
	}

	public class ReferencingCollectionOperation
	{
		public string ReferencingEntityName { get; set; }
		public DateTime? StartedDate { get; set; }
		public DateTime? CompletedDate { get; set; }
		public TimeSpan? ElapsedTime
		{
			get
			{
				if (StartedDate.HasValue)
				{
					if (CompletedDate.HasValue) return CompletedDate.Value.Subtract(StartedDate.Value);
					else return DateTime.UtcNow.Subtract(StartedDate.Value);
				}
				else
				{
					return null;
				}
			}
		}
		public UpdateCascadeOperationStatus Status { get; set; }
		public string ErrorMessage { get; set; }
		public int UpdatedDocumentCount { get; set; }

		public bool IsCompleted
		{

			get
			{
				return Status == UpdateCascadeOperationStatus.Canceled
					|| Status == UpdateCascadeOperationStatus.CompletedSuccessfully
					|| Status == UpdateCascadeOperationStatus.Failed;
			}
		}
	}

	public class UpdateCascadeOperation : IEntity
	{

		public const string IdPrefix = "Raven/UpdateCascadeOperations/";
		public const string EntityName = "UpdateCascadeOperations";
		public const string ByStatusIndexName = "Raven/UpdateCascadeOperations/ByStatus";

		public static IndexDefinition GetByStatusIndexDefinition()
		{
			return new IndexDefinition
			{
				Map = @"from operation in docs.UpdateCascadeOperations select new { Status = operation.Status }",
				Name = ByStatusIndexName
			};
		}

		[JsonIgnore]
		public string Id
		{
			get
			{
				return GetId(ReferencedDocId, ReferencedDocEtag);
			}
		}

		public static string GetId(string referencedDocId, Guid referencedDocEtag)
		{
			return IdPrefix + referencedDocId + '/' + referencedDocEtag.ToString("D");
		}

		[JsonIgnore]
		public Guid? Etag { get; set; }

		public string UpdateCascadeSettingId { get; set; }
		public string ReferencedDocId { get; set; }
		public Guid ReferencedDocEtag { get; set; }
		public int UpdatedDocumentCount
		{
			get
			{
				return this.CollectionOperations.Sum(co => co.UpdatedDocumentCount);
			}
		}
		public DateTime? StartedDate { get; set; }
		public DateTime? CompletedDate { get; set; }
		public TimeSpan? ElapsedTime
		{
			get
			{
				if (StartedDate.HasValue)
				{
					if (CompletedDate.HasValue) return CompletedDate.Value.Subtract(StartedDate.Value);
					else return DateTime.UtcNow.Subtract(StartedDate.Value);
				}
				else
				{
					return null;
				}
			}
		}
		public UpdateCascadeOperationStatus Status { get; set; }
		public bool IsCompleted
		{

			get
			{
				return Status == UpdateCascadeOperationStatus.Canceled
					|| Status == UpdateCascadeOperationStatus.CompletedSuccessfully
					|| Status == UpdateCascadeOperationStatus.Failed;
			}
		}
		
		public List<ReferencingCollectionOperation> CollectionOperations { get; set; }

		public UpdateCascadeOperation()
		{
			CollectionOperations = new List<ReferencingCollectionOperation>();
		}

		public UpdateCascadeOperation(UpdateCascadeSetting setting, string referencedDocId, Guid referencedDocEtag)
		{
			ReferencedDocId = referencedDocId;
			ReferencedDocEtag = referencedDocEtag;
			Status = UpdateCascadeOperationStatus.Pending;
			UpdateCascadeSettingId = setting.Id;
			Etag = Guid.Empty;
			CollectionOperations = new List<ReferencingCollectionOperation>(
				setting.ReferencingCollections.Values.Select(rc => new ReferencingCollectionOperation
				{
					ReferencingEntityName = rc.ReferencingEntityName
				})
			);
		}
	}
}
