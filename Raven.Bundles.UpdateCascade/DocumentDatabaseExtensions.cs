using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Database;
using Raven.Json.Linq;
using Raven.Abstractions.Data;
using Raven.Database.Exceptions;
using Raven.Database.Indexing;
using System.Threading.Tasks;
using System.Threading;
using Raven.Abstractions.Exceptions;
using Raven.Database.Impl;
using Raven.Database.Plugins;
using NLog;

namespace Raven.Bundles.UpdateCascade
{
	public static class DocumentDatabaseExtensions
	{

		private static readonly Logger log = LogManager.GetCurrentClassLogger();

		public static bool IsIndexStale(this DocumentDatabase db, string indexName, DateTime? cutOff, Guid? cuttOfEtag)
		{			
			bool isStale = false;
			db.TransactionalStorage.Batch(actions =>
			{
				isStale = actions.Staleness.IsIndexStale(indexName, cutOff, cuttOfEtag);
			});
			return isStale;
		}

		public static void WaitForIndexNotStale(this DocumentDatabase db, string indexName, DateTime? cutOff, Guid? cuttOfEtag, TimeSpan timeout, CancellationToken cancellationToken)
		{	
			TimeSpan delayIncrement = TimeSpan.FromMilliseconds(50);
			TimeSpan currentDelay = TimeSpan.FromMilliseconds(100);
			DateTime initialTime = DateTime.UtcNow;
			DateTime timeLimit = initialTime.Add(timeout);
			while (db.IsIndexStale(indexName, cutOff, cuttOfEtag))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (DateTime.UtcNow > timeLimit)
				{
					log.Error("WaitForIndexNotStale timed out after {0}", DateTime.UtcNow.Subtract(initialTime));
					throw new TimeoutException("Timeout exceeded waiting for index not to be stale");
				}

				var delayTask = TaskExtensions.Delay(currentDelay, cancellationToken);
				try
				{
					delayTask.Wait();
				}
				catch (Exception ex)
				{

					if (delayTask.IsCanceled)
					{
						log.Trace("WaitForIndexNotStale has been cancelled after {0}", DateTime.UtcNow.Subtract(initialTime));
						throw new OperationCanceledException(cancellationToken);
					}
					else
					{
						log.ErrorException("WaitForIndexNotStale failed", ex);
						throw;
					}
				}
				currentDelay = currentDelay.Add(delayIncrement);
			}
			log.Debug("WaitForIndexNotStale completed in {0}", DateTime.UtcNow.Subtract(initialTime));
		}


		public static IList<string> QueryDocumentIds(this DocumentDatabase db, string index, IndexQuery query, CancellationToken cancellationToken)
		{
			var initialTime = DateTime.UtcNow;
			var loadedIds = new List<string>(query.PageSize);
			db.TransactionalStorage.Batch( _ =>
			{
				var indexStorageQuery = db.IndexStorage.Query(
						index, query, result => true,
						new FieldsToFetch(null, AggregationOperation.None, Constants.DocumentIdFieldName),
						db.IndexQueryTriggers);

				foreach (var queryResult in indexStorageQuery)
				{
					if (cancellationToken.IsCancellationRequested)
					{
						log.Trace("QueryDocumentIds has been canceled");
					}
					cancellationToken.ThrowIfCancellationRequested();
					loadedIds.Add(queryResult.Key);
				}
			});
			log.Trace("QueryDocumentIds returned {0} ids in {1}. Query: {2}, Start: {3}, PageSize: {4}", 
				loadedIds.Count, DateTime.UtcNow.Subtract(initialTime), query.Query, query.Start, query.PageSize);
			return (IList<string>)loadedIds;
		}

		public static void ThrowIfIndexDisabled(this DocumentDatabase db, string indexName)
		{
			db.TransactionalStorage.Batch(actions =>
			{
				var indexFailureInformation = actions.Indexing.GetFailureRate(indexName);
				if (indexFailureInformation.IsInvalidIndex)
				{
					log.Error("{0} index is disabled", indexName);
					throw new IndexDisabledException(indexFailureInformation);
				}
			});
		}

		public static int UpdateByIndex(this DocumentDatabase db, string indexName, IndexQuery query, Func<JsonDocument, bool> updateDoc, 
			TimeSpan waitForIndexNotStaleTimeout, CancellationToken cancellationToken, TransactionInformation transactionInformation)
		{
			try
			{
				var initialTime = DateTime.UtcNow;
				log.Trace("UpdateByIndex started. Index: {0}, Query: {1}, CutoffEtag: {2}", indexName, query.Query, query.CutoffEtag);
				indexName = db.IndexDefinitionStorage.FixupIndexName(indexName);
				db.WaitForIndexNotStale(indexName, query.Cutoff, query.CutoffEtag, waitForIndexNotStaleTimeout, cancellationToken);
				db.ThrowIfIndexDisabled(indexName);
				cancellationToken.ThrowIfCancellationRequested();
				var bulkIndexQuery = new IndexQuery
				{
					Query = query.Query,
					Start = query.Start,
					Cutoff = query.Cutoff,
					CutoffEtag = query.CutoffEtag,
					PageSize = 1024,
					FieldsToFetch = new[] { Constants.DocumentIdFieldName },
					SortedFields = query.SortedFields
				};

				int updatedDocCount = 0;
				IList<string> docIds = null;
				do
				{
					docIds = db.QueryDocumentIds(indexName, bulkIndexQuery, cancellationToken);
					bulkIndexQuery.Start += docIds.Count;
					db.TransactionalStorage.Batch(_ =>
					{
						foreach (var id in docIds)
						{
							cancellationToken.ThrowIfCancellationRequested();
							if (db.UpdateDocumentWithRetries(id, updateDoc, transactionInformation))
							{
								updatedDocCount++;
								log.Debug("{0} document has been updated in UpdateByIndex. {1} documents updated so far", id, updatedDocCount);
							}
						}
					});

				} while (docIds.Count == bulkIndexQuery.PageSize);

				log.Trace("UpdateByIndex completed successfully in {0}. {1} documents updated. Index: {2}, Query: {3}, CutoffEtag: {4}", DateTime.UtcNow.Subtract(initialTime), updatedDocCount, indexName, query.Query, query.CutoffEtag);
				return updatedDocCount;
			}
			catch (OperationCanceledException)
			{
				log.Trace("UpdateByIndex has been canceled");
				throw;
			}
		}

		private static bool UpdateDocumentWithRetries(this DocumentDatabase db, string id, Func<JsonDocument, bool> updateDoc, TransactionInformation transactionInformation)
		{
			int remainingRetries = 64;
			while (true)
			{
				try
				{
					var doc = db.Get(id, transactionInformation);
					if (doc != null && updateDoc(doc))
					{
						db.Put(id, doc.Etag, doc.DataAsJson, doc.Metadata, transactionInformation);
						return true;
					}
					else
					{
						return false;
					}
				}
				catch (ConcurrencyException)
				{
					if (remainingRetries-- <= 0) throw;
					else log.Trace("ConcurrencyException caught in UpdateDocumentWithRetries, remaining retries: {0}", remainingRetries);
				}
			}
		}

		public static Guid GetLastEtag(this DocumentDatabase db)
		{
			Guid lastEtag = Guid.Empty;
			db.TransactionalStorage.Batch(action =>
			{
				lastEtag = action.Staleness.GetMostRecentDocumentEtag();
			});
			log.Debug("Last Etag: {0}", lastEtag);
			return lastEtag;
		}

		public static int UpdateDocumentsAfter(this DocumentDatabase db, Guid eTag, Func<JsonDocument, bool> updateDoc,
			 CancellationToken cancellationToken, TransactionInformation transactionInformation)
		{
			var initialTime = DateTime.UtcNow;
			log.Trace("UpdateDocumentsAfter started whith Etag {0}", eTag);
			int updatedDocCount = 0;
			db.TransactionalStorage.Batch( action =>
				{
					var documentRetriever = new DocumentRetriever(action, db.ReadTriggers);
					foreach (var doc in action.Documents.GetDocumentsAfter(eTag, int.MaxValue))
					{
						DocumentRetriever.EnsureIdInMetadata(doc);
						if (cancellationToken.IsCancellationRequested)
						{
							log.Trace("UpdateDocumentsAfter has been cancelled");
						}
						cancellationToken.ThrowIfCancellationRequested();
						var document = documentRetriever.ExecuteReadTriggers(doc, null, ReadOperation.Load);
						if (document != null && updateDoc(document))
						{
							try
							{
								db.Put(document.Key, document.Etag, document.DataAsJson, document.Metadata, transactionInformation);
								updatedDocCount++;
								log.Debug("{0} document has been updated in UpdateDocumentsAfter. {1} documents updated so far", document.Key, updatedDocCount);
							}
							catch (ConcurrencyException)
							{
								log.Trace("ConcurrencyException caught in UpdateDocumentsAfter");
								if (db.UpdateDocumentWithRetries(document.Key, updateDoc, transactionInformation))
								{
									updatedDocCount++;
									log.Debug("{0} document has been updated in UpdateDocumentsAfter. {1} documents updated so far", document.Key, updatedDocCount);
								}
							}
						}
					}
				});
			log.Trace("UpdateDocumentsAfter ETag {0} completed successfully. {1} documents updated in {2}", eTag, updatedDocCount, DateTime.UtcNow.Subtract(initialTime));
			return updatedDocCount;
		}

		public static IEnumerable<JsonDocument> GetDocsWithIdStartingWith(this DocumentDatabase db, string idPrefix, int start, int pageSize)
		{
			if (idPrefix == null)
				throw new ArgumentNullException("idPrefix");
			idPrefix = idPrefix.Trim();
			var docs = new List<JsonDocument>(pageSize);

			db.TransactionalStorage.Batch(actions =>
			{
				
				var retriever = new DocumentRetriever(actions, db.ReadTriggers);
				foreach (var doc in actions.Documents.GetDocumentsWithIdStartingWith(idPrefix, start, pageSize))
				{
					var document = retriever.ExecuteReadTriggers(doc, null, ReadOperation.Load);
					if (document == null)
						continue;
					docs.Add(document);
				}
				
			});
			return docs;
			
		}

		//private static IEnumerable<JsonDocument> RetrieveDocuments(DocumentRetriever retriever, IEnumerable<JsonDocument> documents)
		//{
		//    foreach (var doc in documents)
		//    {
		//        DocumentRetriever.EnsureIdInMetadata(doc);
		//        var document = retriever.ExecuteReadTriggers(doc, null, ReadOperation.Load);
		//        if (document != null) yield return doc;				
		//    }
		//}
	}
}
