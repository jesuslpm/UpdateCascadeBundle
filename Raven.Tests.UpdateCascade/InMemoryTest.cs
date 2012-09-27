using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Client.Embedded;
using Raven.Client.Document;

namespace Raven.Tests.UpdateCascade
{
	public class InMemoryTest : IDisposable
	{
		protected readonly DocumentStore Store = new EmbeddableDocumentStore();
		//{ 
		//    RunInMemory = true 
		//};

		public InMemoryTest()
		{
			this.Store.Initialize();
		}

		#region IDisposable Members

		public bool IsDisposed { get; private set; }

		public void Dispose()
		{
			if (!IsDisposed)
			{
				IsDisposed = true;
				Dispose(true);
			}
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				Store.Dispose();
			}
		}

		#endregion
	}
}
