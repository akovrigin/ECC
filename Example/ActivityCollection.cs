// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using Mfcom.Cactus809.Core.Adapters;
using Mfcom.Core.Base;

namespace Mfcom.Cactus809.Core.BusinessObjects
{
	[Serializable]
	public class ActivityCollection: BaseCollection<Activity>
	{
		public ActivityCollection()
		{
			m_Adapter = ActivityDSAFactory.Create();
		}

		protected override bool CreateItem(int Index)
		{
			return m_Adapter.Insert(this[Index]) > 0;
		}

		protected override bool UpdateItem(int Index)
		{
			return m_Adapter.Update(this[Index]) > 0;
		}
	}
}