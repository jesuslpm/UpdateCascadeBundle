using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Database;
using Raven.Json.Linq;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;

namespace Raven.Bundles.UpdateCascade
{
	internal class Repository<T> where T: class, IEntity
	{
		private DocumentDatabase db;

		private static readonly string _ravenEntityName = typeof(T).Name + "s";

		protected virtual string RavenEntityName
		{
			get
			{
				return _ravenEntityName;
			}
		}

		public Repository(DocumentDatabase db)
		{
			this.db = db;
		}

		public void Save(T entity, TransactionInformation transactionInformation)
		{
			var metadata = new RavenJObject();
			metadata[Constants.RavenEntityName] = this.RavenEntityName;
			var putResult = db.Put(entity.Id, entity.Etag, JsonExtensions.ToJObject(entity), metadata, transactionInformation);
			entity.Etag = putResult.ETag;
		}

		public T Get(string id)
		{
			var doc = db.Get(id, null);
			if (doc == null) return null;
			var entity = doc.DataAsJson.JsonDeserialization<T>();
			entity.Etag = doc.Etag.Value;
			return entity;
		}

		
		public IEnumerable<T> GetWithIdStartingWith(string idPrefix, int start, int pageSize)
		{
			foreach (var doc in db.GetDocsWithIdStartingWith(idPrefix, start, pageSize))
			{
				var entity = doc.DataAsJson.JsonDeserialization<T>();
				entity.Etag = doc.Etag.Value;
				yield return entity;
			}
		}

		[Obsolete("This is not obsolete, but it is an unbounded result set, so don't call it if you don't knonw what are you doing")]
		internal IEnumerable<T> GetWithIdStartingWith(string idPrefix)
		{
			const int pageSize = 256;
			int docsInPageCount = 0;
			int start = 0;
			do
			{
				docsInPageCount = 0;
				foreach (var entity in GetWithIdStartingWith(idPrefix, start, pageSize))
				{
					docsInPageCount++;
					yield return entity;
				}
				start += docsInPageCount;
			} while (docsInPageCount == pageSize);
		}
	}
}
