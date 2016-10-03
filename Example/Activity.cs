//  --------------------------------------------------------------------
//  Copyright (C) 2012 The Mechanical Frog Project
//  http://www.mfcom.ru
//  --------------------------------------------------------------------

using System;
using System.Xml.Serialization;
using Mfcom.Core.Attributes;
using Mfcom.Core.Base;
using Mfcom.Core.Utils;

namespace Mfcom.Cactus809.Core.BusinessObjects
{
	[Flags]
	public enum InputType
	{
		None = 0,
		GeneralMouse = 1,
		GeneralKeyBoard = 2
	}

	public interface IActivity
	{
		long Id { get; set; }
		long UserId { get; set; }
		DateTime Dt { get; set; }
		string WindowCaption { get; set; }
		string ModuleName { get; set; }
		int HasInput { get; set; }
	}

	[Serializable]
	public class Activity: BaseItem, IActivity
	{
		private DateTime m_Dt = NullDate.Value;
		private int m_HasInput;
		private string m_ModuleName = "";
		private long m_UserId;
		private string m_WindowCaption = "";

		#region IActivity Members

		[XmlElement("UserId")]
		[MapField("UserId", "UserId")]
		public long UserId
		{
			get { return m_UserId; }
			set
			{
				if (m_UserId == value)
					return;

				m_UserId = value;
				MakeDirty();

				NotifyPropertyChanged("UserId");
			}
		}

		[XmlElement("Dt")]
		[MapField("Dt", "Dt", DateHasTimePart = true)]
		public DateTime Dt
		{
			get { return m_Dt; }
			set
			{
				if (m_Dt == value)
					return;

				m_Dt = value;
				MakeDirty();

				NotifyPropertyChanged("Dt");
			}
		}

		[XmlElement("WindowCaption")]
		[MapField("WindowCaption", "WindowCaption")]
		public string WindowCaption
		{
			get { return m_WindowCaption; }
			set
			{
				if (m_WindowCaption == value)
					return;

				m_WindowCaption = value;
				MakeDirty();

				NotifyPropertyChanged("WindowCaption");
			}
		}

		[XmlElement("ModuleName")]
		[MapField("ModuleName", "ModuleName")]
		public string ModuleName
		{
			get { return m_ModuleName; }
			set
			{
				if (m_ModuleName == value)
					return;

				m_ModuleName = value;
				MakeDirty();

				NotifyPropertyChanged("ModuleName");
			}
		}

		[XmlElement("HasInput")]
		[MapField("HasInput", "HasInput")]
		public int HasInput
		{
			get { return m_HasInput; }
			set
			{
				if (m_HasInput == value)
					return;

				m_HasInput = value;
				MakeDirty();

				NotifyPropertyChanged("HasInput");
			}
		}

		#endregion
	}
}