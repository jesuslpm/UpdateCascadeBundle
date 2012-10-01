# Update Cascade Bundle 

## Keep your RavenDB denormalized references up to date automatically

## Features:
* Works on RavenDb 1.2
* Updates referencing entities in the background
* Resilient to server restarts and crashes. The bundle restarts all pending cascade operations upon server start.
* Participates in the shutdown process, canceling all in progress cascade operations.
* Trackable: each update cascade operation has a corresponding document in Raven/UpdateCascadeOperations
* Debuggable: you can configure log NLog.config


## Guidelines

### Do not use this bundle when:
* The referenced entity is updated often.
* There are many referencing entities.

### You may use this bundle when:
* The referenced entity is updated rarely.
* There are not too many referencing entities.

## Benefits
* You can search and order a referencing entity by referenced entity properties.

## Alternatives
* Includes
* Multimap indexes


## FAQ's

Q. How do I configure a denormalized reference to be updated automatically when the referenced entity changes?
A. 
	1. The denormalized reference must include id and Etag properties of the referenced entity. 
	2. You need to create an index on the referencing entity that includes the referenced id and Etag.
	3. You need to store a UpdateCascadeSetting object with the details.

Q. How do you update referencing entities?
A.
   I span a new update cascade operation in a put trigger. The update cascade operation runs in a separate thread and in another transacion. I scan the specified index searching for referencing entities and update them.

Q. How do you handle index staleness?
A. 
	Waiting for the index no to be stale as of last document Etag, then scaning all documents updated/inserted after that Etag.

Q. What happens when I update an entity that has an in progress cascade operation?
A.
	The in progress cascade operation is canceled, and a new cascade operation is started.

Q. What happens then the server shutsdown or crashes in the middle of a cascade operation?
A.
	When the server shutsdown all in progress cascade operations are canceled.
	When the server crashes I can do nothing, but there is no problem at all.
	When the server restarts all pending cascade operations are restarted.


## Setting up cascade operation sample:

Given the model:

namespace Raven.Tests.UpdateCascade
{
	public class Category
	{
		public string Id { get; set; }
		[RavenIgnore]
		public Guid Etag { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
	}

	public class CategoryRef
	{
		public string Id { get; set; }
		public Guid Etag { get; set; }
		public string Name { get; set; }
		public static implicit operator CategoryRef(Category category)
		{
			return new CategoryRef
			{
				Etag = category.Etag,
				Id = category.Id,
				Name = category.Name
			};
		}
	}

	public class Product
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public CategoryRef Category { get; set; }
		public decimal UnitPrice { get; set; }
	}
}

You need to setup this index:

	public class Products_ByCategoryId : AbstractIndexCreationTask<Product>
	{
		public Products_ByCategoryId()
		{
			Map = products => from p in products
							  where p.Category != null
							  select new
							  {
								  CategoryId = p.Category.Id,
								  Etag = p.Category.Etag
							  };

		}

	}

And store this document:

var setting = new UpdateCascadeSetting
{
	ReferencedEntityName = "Categories",
	DenormalizedReferencePropertyNames = new List<string>
	{
		"Name"
	},
	ReferencingCollections = new Dictionary<string, ReferencingCollectionSetting>
	{
		{ 
			"Products", 
			new ReferencingCollectionSetting  
			{ 
				IndexName = "Products/ByCategoryId",
				ReferencingEntityName = "Products",
				ReferencingPropertyPath = "Category",
				ReferencedEtagPropertyNameInIndex = "Etag",
				ReferencedIdPropertyNameInIndex = "CategoryId"							
			}
		}
	}
};

## Update cascade operation document sample

Here you have an example of an update cascade operation document. The Id of update cascade operation documents is as follows:

"Raven/UpdateCascadeOperations/" + ReferencedDocumentId + "/" + ReferencedDocumentEtag. For example:

Id: Raven/UpdateCascadeOperations/Categories/1/00000000-0000-0100-0000-000000000039

{
  "UpdateCascadeSettingId": "Raven/UpdateCascadeSettings/Categories",
  "ReferencedDocId": "Categories/1",
  "ReferencedDocEtag": "00000000-0000-0100-0000-000000000039",
  "UpdatedDocumentCount": 10,
  "StartedDate": "2012-10-01T07:09:51.2234342Z",
  "CompletedDate": "2012-10-01T07:09:52.9654342Z",
  "ElapsedTime": "00:00:01.7420000",
  "Status": "CompletedSuccessfully",
  "IsCompleted": true,
  "CollectionOperations": [
    {
      "ReferencingEntityName": "Products",
      "StartedDate": "2012-10-01T07:09:51.2294342Z",
      "CompletedDate": "2012-10-01T07:09:52.9454342Z",
      "ElapsedTime": "00:00:01.7160000",
      "Status": "CompletedSuccessfully",
      "ErrorMessage": null,
      "UpdatedDocumentCount": 10,
      "IsCompleted": true
    }
  ]
}
	









