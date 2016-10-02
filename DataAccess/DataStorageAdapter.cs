// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System.Data;
using Mfcom.Core.Base;
using Mfcom.Core.Utils;

namespace Mfcom.Core.DataAccess
{
	/// <summary>
	/// ����������� ������� ��� �������� DataStorageAdapter.
	/// </summary>
	//[Serializable]
	public abstract class DataStorageAdapterFactory
	{
		protected static DataStorageType m_StorageType;

		static DataStorageAdapterFactory()
		{
			m_StorageType = BaseSolutionProperties.DataStorageType;
		}
	}

	public interface IDataStorageAdapter
	{
		/// <summary>
		/// �������� ��� ������ �������
		/// </summary>
		/// <returns>������� �����</returns>
		DataTable Select();

		/// <summary>
		/// �������� ���� ������ �� �����
		/// </summary>
		/// <param name="Id">����</param>
		/// <returns>������� � ����� ������� ��� ������</returns>
		DataTable Select(long Id);

		/// <summary>
		/// ����� ����� � �������
		/// </summary>
		/// <returns></returns>
		int Count();

		/// <summary>
		/// ���������� �����, ���������� �������� �� ��������� (select Top X * from table)
		/// </summary>
		void SetTopN(int Count);

		/// <summary>
		/// ��������� ����� ������ � ��������� � ���������� ��� ������������� (Id)
		/// </summary>
		/// <param name="Param">����������� ������</param>
		/// <returns>� ������ ������ ���������� ����� ������������� ��� ������ ��� ����������� �������</returns>
		long Insert(BaseItem Param);

		int Update(BaseItem Param);

		int Delete(long Param);

		void BeginTransaction();
		void EndTransaction();
		void RollBack();

		/// <summary>
		/// ��������� �� � ����������. ��� ����� �������.
		/// </summary>
		/// <returns></returns>
		bool IsInTransaction();
	}
}