using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Client.Indexes;

namespace Raven.Tests.UpdateCascade
{
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
}
