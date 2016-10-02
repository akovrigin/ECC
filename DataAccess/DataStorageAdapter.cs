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
	/// Абстрактная фабрика для создания DataStorageAdapter.
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
		/// Получить все строки таблицы
		/// </summary>
		/// <returns>Таблица строк</returns>
		DataTable Select();

		/// <summary>
		/// Получить одну строку по ключу
		/// </summary>
		/// <param name="Id">Ключ</param>
		/// <returns>Таблица с одной строкой или пустая</returns>
		DataTable Select(long Id);

		/// <summary>
		/// Число строк в таблице
		/// </summary>
		/// <returns></returns>
		int Count();

		/// <summary>
		/// Количество строк, получаемых запросом из хранилища (select Top X * from table)
		/// </summary>
		void SetTopN(int Count);

		/// <summary>
		/// Сохраняет новый объект в хранилище и возвращает его идентификатор (Id)
		/// </summary>
		/// <param name="Param">Сохраняемый объект</param>
		/// <returns>В случае успеха возвращает новый идентификатор для только что сохранённого объекта</returns>
		long Insert(BaseItem Param);

		int Update(BaseItem Param);

		int Delete(long Param);

		void BeginTransaction();
		void EndTransaction();
		void RollBack();

		/// <summary>
		/// Находится ли в транзакции. Для целей отладки.
		/// </summary>
		/// <returns></returns>
		bool IsInTransaction();
	}
}