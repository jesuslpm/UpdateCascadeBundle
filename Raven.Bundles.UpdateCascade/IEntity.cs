using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Raven.Bundles.UpdateCascade
{
	public interface IEntity
	{
		string Id { get; }
		Guid? Etag { get; set; }
	}
}
