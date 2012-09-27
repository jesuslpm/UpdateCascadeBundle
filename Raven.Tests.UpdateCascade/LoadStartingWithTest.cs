using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Raven.Tests.UpdateCascade
{
	public class LoadStartingWithTest : InMemoryTest
	{

		const int categoryCount = 5;
		const int productsPerCategory = 10;

		[Fact]
		public void LoadStatingWithShouldWork()
		{
			StoreSomeProductsAndCategories();

			using (var session = Store.OpenSession())
			{
				var products = session.Advanced.LoadStartingWith<Product>("Categories/1/Products/");
				Assert.Equal(productsPerCategory, products.Count());

			}
		}

		private void ModifySomeProducts()
		{
			using (var session = this.Store.OpenSession())
			{
				var products = session.Advanced.LoadStartingWith<Product>("Categories/1/Products/");
				Assert.Equal(productsPerCategory, products.Count());
				foreach (var p in products)
				{
					p.Category.Name += " New Name";
				}
				session.SaveChanges();
			}
		}

		private void StoreSomeProductsAndCategories()
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

					session.Store(category, category.Etag);
					categories.Add(category);
				}

				session.SaveChanges();

				foreach (var category in categories)
				{
					category.Etag = session.Advanced.GetMetadataFor(category).Value<Guid>("@etag");

					for (int productNumber = 1; productNumber <= productsPerCategory; productNumber++)
					{
						var product = new Product
						{
							Id = category.Id + "/Products/" + productNumber.ToString(),
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
