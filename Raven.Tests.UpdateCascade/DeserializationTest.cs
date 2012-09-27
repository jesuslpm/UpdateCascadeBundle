using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Raven.Json.Linq;
using Raven.Abstractions.Extensions;

namespace Raven.Tests.UpdateCascade
{
	public class Foo
	{
		public string Id { get; set; }
		public Guid Guid { get; set; }
	}

	public class DeserializationTest
	{

		[Fact]
		public void CanDeserializeGuids()
		{
			var foo1 = new Foo { Guid = Guid.NewGuid() };
			RavenJObject doc = RavenJObject.FromObject(foo1);
			var foo2 = doc.JsonDeserialization<Foo>();
			Assert.Equal(foo1.Guid, foo2.Guid);
		}
	}
}
