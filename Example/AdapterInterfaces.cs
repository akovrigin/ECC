// --------------------------------------------------------------------
// Copyright (C) 2003-2011 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------

using System;
using System.Data;
using Mfcom.Cactus809.Core.BusinessObjects;
using Mfcom.Core.DataAccess;

namespace Mfcom.Cactus809.Core.Adapters
{
	public interface IActivityAdapter : IDataStorageAdapter
	{
		DataTable Select(long? Id, long? UserId, DateTime? Dt, string WindowCaption, string ModuleName);
	}
}