using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RavenIgnore = Raven.Imports.Newtonsoft.Json.JsonIgnoreAttribute;


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
