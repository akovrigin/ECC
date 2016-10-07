// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using Mfcom.Core.Actualizers;
using Mfcom.Core.Attributes;
using Mfcom.Core.DataAccess;
using Mfcom.Replication;

namespace Mfcom.Core.Base
{
	public class UpdateStatusEventArgs
	{
		#region UpdateState enum

		public enum UpdateState
		{
			Unknown,
			Set,
			Reset
		}

		#endregion

		private readonly object m_Sender;
		private readonly UpdateState m_State;

		public UpdateStatusEventArgs(object Sender, UpdateState State)
		{
			m_State = State;
			m_Sender = Sender;
		}

		public UpdateState State
		{
			get {return m_State;}
		}

		public object Target
		{
			get {return m_Sender;}
		}
	}

	public delegate void UpdateStatusHandler(object Sender, UpdateStatusEventArgs Args);

	/// <summary>
	/// îñíîâà èåðàðõèè ñóùíîñòåé. Ïîòîìêè ýòîãî êëàññà õðàíÿòñÿ â 
	/// ñîîòâåòñòâóþùèõ ñóøíîñòÿì êîëëåêöèÿõ.
	/// </summary>
	[Serializable]
	public class BaseItem: IComparer,
	                       IComparable,
	                       ICloneable,
	                       IUpdateFlagsCalculator,
	                       INotifyPropertyChanged
	{
		protected static long m_JustCreatedId;

		/// <summary>
		/// Ïðèçíàê òîãî, ÷òî îáúåêò èñïîëüçóåòñÿ â êà÷åñòâå òðàíñïîðòíîãî
		/// </summary>
		private bool m_ClassStorage;

		/// <summary>
		/// Ïîëå, ñîäåðæàùåå çíà÷åíèå ñâîéñòâà Id
		/// </summary>
		private long m_Id = -1;

		/// <summary>
		/// Ïîëå, ñîäåðæàùåå çíà÷åíèå ñâîéñòâà Deleted
		/// </summary>
		private bool m_IsDeleted;

		/// <summary>
		/// Ïîëå, ñîäåðæàùåå çíà÷åíèå ñâîéñòâà Updated
		/// </summary>
		private bool m_IsUpdated;

		/// <summary>
		/// Ïðèçíàê òîãî, ÷òî îáúåêò äîëæåí áûòü âîññîçäàí íà äðóãîé ñòîðîíå êàíàëà âíå çàâèñèìîñòè îò çíà÷åíèé ôëàãîâ. 
		/// </summary>
		private SqlAction m_ReplicateVia = SqlAction.Nothing;

		/// <summary>
		/// Ñáðàñûâàÿ è óñòàíàâëèâàÿ ýòîò ôëàã, ìîæíî îòñëåæèâàòü èçìåíåíèå
		/// ñâîéñòâ îáúåêòà â ïðîìåæóòêå îò åãî ñîçäàíèÿ äî ñîõðàíåíèÿ
		/// </summary>
		[NonSerialized] private bool m_WasChangedInProcess;

		protected bool m_WriteToMemory;

		/// <summary>
		/// Ïîëüçîâàòåëüñêèå èäåíòèôèêàòîðû áóäóò íà÷èíàòüñÿ ñ 100000. Äî ýòîãî - ñëóæåáíûå.
		/// </summary>
		private const int m_UserId = 100000;

		public BaseItem()
		{
			if ((m_Id = --m_JustCreatedId) > -1) // Id has been wrapped around
				Alert();

			if ((m_Id % m_UserId) == 0 && (m_Id / m_UserId) > 0) // ìîíèòîðèòü, íà÷èíàÿ ñî 100000
				Monitor();
		}

		protected BaseItem(SerializationInfo Info, StreamingContext Context)
		{
			Info.SetType(GetType());
			MemberInfo[] mis = FormatterServices.GetSerializableMembers(GetType());

			for(int a = 0; a < mis.Length; a++)
			{
				var info = (FieldInfo)mis[a];
				info.SetValue(this, Info.GetValue(info.Name, info.FieldType));
			}
		}

		public BaseItem(long Id)
		{
			m_Id = Id;
		}

		public BaseItem(BaseItem Item)
		{
			m_Id = Item.Id;
			m_IsDeleted = Item.IsDeleted;
			m_IsUpdated = Item.IsUpdated;
			m_ReplicateVia = Item.m_ReplicateVia;
			m_WasChangedInProcess = Item.m_WasChangedInProcess;
			m_WriteToMemory = Item.m_WriteToMemory;
			m_ClassStorage = Item.m_ClassStorage;
		}

		/// <summary>
		/// Id - óíèêàëüíûé èäåíòèôèêàòîð ñóùíîñòè. Ýòî çíà÷åíèå èñïîëüçóåòñÿ äëÿ 
		/// èäåíòèôèêàöèè ýëåìåíòà â ñîîòâåòñòâóþùåé êîëëåêöèè. Ó òîëüêî ÷òî ñîçäàííîãî 
		/// îáúåêòà ýòîò èäåíòèôèêàòîð - îòðèöàòåëüíûé, îäíàêî äëÿ ïðîâåðêè òîãî, ÷òî îáúåêò
		/// ÿâëÿåòñÿ íîâûì - ïðîâåðÿéòå ïîëå IsNew, à íå çíà÷åíèå Id
		/// </summary>
		[MapField("Id", "Id", Propagate = true)]
		public long Id
		{
			get {return m_Id;}
			set {m_Id = value;}
		}

		/// <summary>
		/// Ïðèçíàê îòìå÷åííîãî ê óäàëåíèþ îáúåêòà. Ïðè âûçîâå â ñîîòâåòñòâóþùåé êîëëåêöèè 
		/// ìåòîäà Update ïðè óñòàíîâëåííîì ôëàãå áóäåò âûïîëíåíî óäàëåíèå îáúåêòà èç õðàíèëèùà 
		/// (ÁÄ, íàïðèìåð). Äî ìîìåíòà îáðàùåíèÿ ê õðàíèëèùó óäàë¸ííûé îáúåêò ñîäåðæèòñÿ â êîëëåêöèè 
		/// è åãî óäàëåíèå ìîæíî îòêàòèòü.
		/// </summary>
		public bool IsDeleted
		{
			get {return m_IsDeleted;}
			set {m_IsDeleted = value;}
		}

		/// <summary>
		/// Ïðèçíàê òîãî, ÷òî äàííûå îáúåêòà ìåíÿëèñü ñ ìîìåíòà åãî ñîçäàíèÿ èëè ñ ïîñëåäíåãî âûçîâà
		/// ìåòîäà MakeIntact
		/// </summary>
		public bool IsUpdated
		{
			get {return m_IsUpdated;}
			set
			{
				m_IsUpdated = value;

				if (null != UpdateStateChanged)
					UpdateStateChanged(this, new UpdateStatusEventArgs(this,
						value ? UpdateStatusEventArgs.UpdateState.Set:UpdateStatusEventArgs.UpdateState.Reset));
			}
		}

		/// <summary>
		/// Èñïîëüçóåòñÿ â êà÷åñòâå ïðèçíàêà èçìåíåíèÿ äàííûõ îáúåêòà â ëþáîì 
		/// ïðîìåæóòêå âðåìåíè. Äîëæåí ñáðàñûâàòüñÿ âðó÷íóþ.
		/// </summary>
		public bool WasChangedInProcess
		{
			set {m_WasChangedInProcess = value;}
			get {return m_WasChangedInProcess || HasCollections() || (this is IHasCollections);}
		}

		/// <summary>
		/// Áûëè ëè ìîäèôèöèðîâàíû ñâîéñòâà îáúåêòà?
		/// </summary>
		public bool IsModified
		{
			get {return IsUpdated || IsDeleted;}
		}

		/// <summary>
		/// ßâëÿåòñÿ ëè îáúåêò íîâûì, íå ñîõðàíÿâøèìñÿ â êîëëåêöèè
		/// </summary>
		public bool IsNew
		{
			get {return m_Id <= 0;}
		}

		/// <summary>
		/// Åñëè óñòàíîâëåí â true, òî ïðè ñîõðàíåíèè îáúåêòà êîëëåêöèåé ÷åðåç âûçîâ Update îáúåêò íå áóäåò ñîõðàíÿòüñÿ â ÁÄ.
		/// Ïðè ýòîì ôëàæêè-ïðèçíàêè íå î÷èùàþòñÿ
		/// </summary>
		public virtual bool DeferDBOperations
		{
			get {return m_WriteToMemory;}
			set {m_WriteToMemory = value;}
		}

		/// <summary>
		/// Ñáðàñûâàÿ è óñòàíàâëèâàÿ ýòîò ôëàã ìîæíî îáõîäèòü áèçíåñ-ïðàâèëà
		/// </summary>
		public bool ClassStorage
		{
			get {return m_ClassStorage;}
			set {m_ClassStorage = value;}
		}

		/// <summary>
		/// Òèï îïåðàöèè, ïîðîæäàåìîé ïðè îáðàáîòêå îáúåêòà â ïðîöåññå ðåïëèêàöèè íà ïðèíèìàþùåé ñòîðîíå.
		/// Çàäà¸òñÿ îòïðàâèòåëåì.
		/// </summary>
		public SqlAction ReplicateVia
		{
			get {return m_ReplicateVia;}
			set {m_ReplicateVia = value;}
		}

		#region ICloneable Members

		/// <summary>
		/// Ñîçäàòü êîïèþ îáúåêòà ïî äàííîìó
		/// </summary>
		/// <returns>Êîïèÿ îáúåêòà</returns>
		public object Clone()
		{
			using(var stream = new MemoryStream())
			{
				//SoapFormatter formatter = new SoapFormatter();
				Type type = GetType();
				var formatter = new XmlSerializer(type);
				//BinaryFormatter formatter = new BinaryFormatter();
				formatter.Serialize(stream, this);

				stream.Position = 0;
				object clone = formatter.Deserialize(stream);

				((BaseItem)clone).WasChangedInProcess = false;

				return clone;
			}
		}

		#endregion

		#region IComparable Members

		public int CompareTo(object obj)
		{
			BaseItem _X = this;
			var _Y = obj as BaseItem;

			if (_X.m_Id < _Y.m_Id)
				return -1;

			if (_X.m_Id == _Y.m_Id)
				return 0;

			return 1;
		}

		#endregion

		#region IComparer Members

		/// <summary>
		/// Èñïîëüçóåòñÿ äëÿ óïîðÿäî÷èâàíèÿ îáúåêòîâ â êîëëåêöèè ïî Id
		/// </summary>
		/// <param name="X"></param>
		/// <param name="Y"></param>
		/// <returns></returns>
		public int Compare(object X, object Y)
		{
			var _X = X as BaseItem;
			var _Y = Y as BaseItem;

			if (_X.m_Id < _Y.m_Id)
				return -1;

			if (_X.m_Id == _Y.m_Id)
				return 0;

			return 1;
		}

		#endregion

		#region INotifyPropertyChanged Members

		[field: NonSerialized]
		public event PropertyChangedEventHandler PropertyChanged;

		#endregion

		#region IUpdateFlagsCalculator Members

		/// <summary>
		/// Îïðåäåëÿåò, ÷òî íóæíî äåëàòü ïðè ñîõðàíåíèè äàííîãî îáúåêòà. Âîçìîæíûå âàðèàíòû:
		/// * Âñòàâêà
		/// * Îáíîâëåíèå
		/// * Óäàëåíèå
		/// </summary>
		/// <returns>×òî íóæíî äåëàòü ïðè ñîõðàíåíèè</returns>
		public SqlAction GetOperationType()
		{
			return HandleOwnUpdate();
		}

		#endregion

		[field: NonSerialized]
		public event UpdateStatusHandler UpdateStateChanged;

		/// <summary>
		/// Ñáðîñèòü âñå âíóòðåííèå ôëàãè îáúåêòà, êàê áóäòî ñîñòîÿíèå îáúåêòà 
		/// íå ìåíÿëîñü ïîñëå çàãðóçêè èç ÁÄ. Òîëüêî ïîëå IsNew ñîõðàíÿåò ñâî¸ ñîñòîÿíèå
		/// </summary>
		public void MakeIntact()
		{
			m_IsDeleted = false;
			IsUpdated = false;
			ReplicateVia = SqlAction.Nothing;
		}

		/// <summary>
		/// Îòìåòèòü îáúåêò, êàê ïîäëåæàùèé èçìåíåíèþ â õðàíèëèùå.
		/// </summary>
		public void MakeDirty()
		{
			IsUpdated = true;
			m_WasChangedInProcess = true;
		}

		/// <summary>
		/// Ñîäåðæèò ëè îáúåêò âíóòðåííèå êîëëåêöèè, òðåáóþùèå ñîõðàíåíèÿ
		/// </summary>
		/// <returns></returns>
		public virtual bool HasCollections()
		{
			return false;
		}

		private void Alert()
		{
			// Ñîîáùèòü î òîì, ÷òî âíîâü ñîçäàâàåìûå ýëåìåíòû áîëüøå íå óíèêàëüíû 
		}

		private void Monitor()
		{
			// Äèàãíîñòèêà äîñòèæåíèÿ î÷åðåäíîé ãðàíèöû â 100000 èçðàñõîäîâàííûõ îáúåêòîâ
		}

		/// <summary>
		/// Ìåòîä îïðåäåëÿåò ïîâåäåíèå êîëëåêöèè, ñîäåðæàùåé ýëåìåíòû, â ïðîöåññå ñîõðàíåíèÿ íà ñëó÷àé, åñëè
		/// íàäî âìåñòî âñòàâêè âûïîëíèòü îáíîâëåíèå èëè íè÷åãî íå äåëàòü.
		/// </summary>
		/// <returns>Äåéñòâèå, êîòîðîå íåîáõîäèìî âûïîëíèòü äëÿ îáúåêòà</returns>
		protected virtual SqlAction HandleOwnUpdate()
		{
			var _this = this as IIdDoesExist;
			bool exists = false;

			if (null != _this && !IsNew)
				exists = _this.Exists(Id);

			if (ReplicateVia == SqlAction.Nothing)
				return new DefaultUpdateFlagsCalculator().GetOperationType(this);

			if (ReplicateVia != SqlAction.Delete && exists)
				ReplicateVia = SqlAction.Update;

			if (ReplicateVia == SqlAction.Update)
				MakeDirty();

			return new DefaultUpdateFlagsCalculator().GetOperationType(this);
		}

		public virtual void CollectionIsDirty(object Sender, UpdateStatusEventArgs Args)
		{
			if (Args.State == UpdateStatusEventArgs.UpdateState.Set)
				IsUpdated = true;
			else if (Args.State == UpdateStatusEventArgs.UpdateState.Reset)
			{
				//Ðîäèòåëüñêèé îáúåêò ìîæåò îñòàâàòüñÿ "ãðÿçíûì", äàæå åñëè äî÷åðíèé ñòàë "÷èñòûì"
			}
			else
				throw new InvalidEnumArgumentException(String.Format("{0} is unexpected", Args.State));
		}

		protected void NotifyPropertyChanged(string PropertyName)
		{
			if (PropertyChanged != null)
				PropertyChanged(this, new PropertyChangedEventArgs(PropertyName));
		}
	}
}
