using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Bundles.UpdateCascade;
using Xunit;
using Raven.Client;
using System.Threading;
using System.Threading.Tasks;

namespace Raven.Tests.UpdateCascade
{

	public class UpdateCascadeTest
	{
		[Fact]
		public void ShouldWorkInMemory()
		{
			var test = new ConfigurableUpdateCascadeTest(true);
			test.UpdateCascadeShouldWork();
		}

		[Fact]
		public void ShouldWorkInEsent()
		{
			var test = new ConfigurableUpdateCascadeTest(false);
			test.UpdateCascadeShouldWork();
		}

	}

	public class ConfigurableUpdateCascadeTest : RavenStoreTest
	{
		public ConfigurableUpdateCascadeTest(bool runInMemory): base(runInMemory)
		{
			new Products_ByCategoryId().Execute(this.Store);
			FillStore();
			SetUpUpdateCascadeSettings();
		}

		public void UpdateCascadeShouldWork()
		{
			using (var session = this.Store.OpenSession())
			{
				var category = session.Load<Category>("Categories/1");
				var newName = category.Name + " new Name";
				category.Name = newName;
				session.SaveChanges();
				category.Etag = session.Advanced.GetMetadataFor(category).Value<Guid>("@etag");
				UpdateCascadeOperation operation = null;

				var waitTaks = Task.Factory.StartNew(() =>
				{
					operation = WaitOperationToComplete(category);
				});

				if (!waitTaks.Wait(TimeSpan.FromSeconds(1200)))
				{
					throw new TimeoutException("The cascade operation did not completed within the allotted time");
				}



				Assert.Equal(UpdateCascadeOperation.GetId(category.Id, category.Etag.Value), operation.Id);
				Assert.Equal(true, operation.IsCompleted);
				Assert.Equal(1, operation.CollectionOperations.Count);
				Assert.Equal(category.Etag, operation.ReferencedDocEtag);
				Assert.Equal(category.Id, operation.ReferencedDocId);
				Assert.Equal(UpdateCascadeOperationStatus.CompletedSuccessfully, operation.Status);
				Assert.Equal(UpdateCascadeSetting.GetId("Categories"), operation.UpdateCascadeSettingId);
				Assert.Equal(10, operation.UpdatedDocumentCount);

				for (int productNumber = 1; productNumber <= productsPerCategory; productNumber++)
				{
					var productId = category.Id + "/Products/" + productNumber.ToString();
					var product = session.Load<Product>(productId);
					Assert.Equal(category.Name, product.Category.Name);
					Assert.Equal(category.Etag, product.Category.Etag);
				}
			}
		}

		private UpdateCascadeOperation WaitOperationToComplete(Category category)
		{
			while (true)
			{
				using (var session = this.Store.OpenSession())
				{
					var operation = session.Load<UpdateCascadeOperation>(UpdateCascadeOperation.GetId(category.Id, category.Etag.Value));
					if (operation != null && operation.IsCompleted)
					{
						return operation;
					}
					Thread.Sleep(10);
				}
			}
		}

		public void SetUpUpdateCascadeSettings()
		{
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

			using (var session = this.Store.OpenSession())
			{
				session.Store(setting);
				session.SaveChanges();

				var s = session.Advanced.DocumentStore.DatabaseCommands.Get(setting.Id);
			}
		}

		const int categoryCount = 5;
		const int productsPerCategory = 10;



		public void FillStore()
		{
			using (var session = this.Store.OpenSession())
			{

				var categories = new List<Category>(categoryCount);
				for (int categoryNumber = 1; categoryNumber <= categoryCount; categoryNumber++)
				{
					var category = new Category
					{
						Id = "Categories/" + categoryNumber.ToString(),
						Name = "Category " + categoryNumber.ToString(),
						Description = "Category " + categoryNumber.ToString() + " description"
					};

					session.Store(category);
					categories.Add(category);
				}

				session.SaveChanges();

				foreach (var category in categories)
				{
					category.Etag =  session.Advanced.GetMetadataFor(category).Value<Guid>("@etag");

					for (int productNumber = 1; productNumber <= productsPerCategory; productNumber++)
					{
						var product = new Product
						{
							Id =  category.Id + "/Products/" + productNumber.ToString(),
							Category = category,
							Name = "Product " + category.Id + "/Products/" + productNumber.ToString(),
							UnitPrice = productNumber
						};

						session.Store(product);
					}
				}
				session.SaveChanges();
			}
		}
	}
}
