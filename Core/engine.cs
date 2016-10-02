// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Collections;
using System.Data;
using System.Reflection;
using Mfcom.Core.Attributes;
using Mfcom.Core.DataAccess;
using Mfcom.Core.Utils;

namespace Mfcom.Core.Base
{
	public abstract class BaseEngine
	{
		private readonly Hashtable m_Cache = Hashtable.Synchronized(new Hashtable());
		protected IDataStorageAdapter m_Adapter;

		/// <summary>
		/// Быстрое получение числа сущностей в соответствующем хранилище (таблице, представлении и т.п.)
		/// без загрузки самих сущностей в коллекцию.
		/// </summary>
		public int Count
		{
			get {return m_Adapter.Count();}
		}

		[Obsolete]
		protected DateTime LoadNullableDateTime(DataRow R, string ColumnName)
		{
			return R.IsNull(ColumnName)? NullDate.Value: Convert.ToDateTime(R[ColumnName]);
		}

		/// <summary>
		/// Вспомогательный метод. Выполняет основную работу по заполнению объекта данными на основании
		/// атрибутов, навешенных на этот объект.
		/// </summary>
		/// <param name="Row">Строка, из которой надо получать данные</param>
		/// <returns>Заполненный объект</returns>
		protected virtual T Make<T>(DataRow Row) where T : BaseItem, new()
		{
			var instance = new T();
			return Make(Row, instance);
		}

		protected virtual T Make<T>(DataRow Row, T Target) where T : BaseItem
		{
			T instance = Target;
			PropertyInfo[] props = instance.GetType().GetProperties();

			foreach(PropertyInfo prop in props)
			{
				var atts = (MapFieldAttribute[])m_Cache[prop.Name];
				if (atts == null)
				{
					atts = (MapFieldAttribute[])prop.GetCustomAttributes(typeof(MapFieldAttribute), false);
					m_Cache.Add(prop.Name, atts);
				}

				if (atts.Length != 1)
					continue;
				// BugzID: 557
				if ((atts[0].SkipFor & AdapterOperations.Select) == AdapterOperations.Select)
					continue;

				object val = Preprocess(Row, prop.PropertyType, atts[0].FieldName);

				SetHandler<T> setter = LightweightCompilation<T>.CreateSetHandler(prop);
				setter(instance, val);
			}

			instance.MakeIntact();
			return instance;
		}

		/// <summary>
		/// Приведение считанных из хранилища полей к типам CTS
		/// </summary>
		/// <param name="Row">Строка, из которой получать поля</param>
		/// <param name="MemberType">Тип свойства объекта</param>
		/// <param name="FieldName">Имя поля в строке</param>
		/// <returns></returns>
		private object Preprocess(DataRow Row, Type MemberType, string FieldName)
		{
			object val = Row[FieldName];

            if (val is DBNull)
            {
                if (MemberType == typeof(Int32))
                    return 0;
                if (MemberType == typeof(Int32?))
                    return null;
                if (MemberType == typeof(Int64))
                    return 0L;
                if (MemberType == typeof(Int64?))
                    return null;
                if (MemberType == typeof(Decimal) || MemberType == typeof(Decimal?))
                    return NullDecimal.Value;
                if (MemberType == typeof(DateTime) || MemberType == typeof(DateTime?))
                    return NullDate.Value;
				if (MemberType == typeof(TimeSpan))
					return NullTime.Value;
                return MemberType == typeof(String)? null: TryUserConversionForNull(FieldName);
            }

		    if (MemberType == typeof(Int32) || MemberType == typeof(Int32?))
				return Convert.ToInt32(val);
            if (MemberType == typeof(Int64) || MemberType == typeof(Int64?))
				return Convert.ToInt64(val);
            if (MemberType == typeof(DateTime) || MemberType == typeof(DateTime?))
				return Convert.ToDateTime(val);
            if (MemberType == typeof(Boolean) || MemberType == typeof(Boolean?))
				return Convert.ToBoolean(val);
            if (MemberType == typeof(Decimal) || MemberType == typeof(Decimal?))
				return Convert.ToDecimal(val);
			if (MemberType == typeof(String))
				return Convert.ToString(val);
			if (MemberType == typeof(TimeSpan))
				return TimeSpan.Parse(val.ToString());

			return TryUserConversion(FieldName, val);
		}

		/// <summary>
		/// Шанс для пользовательского engine на такую обработку значения поля, которая нужна. Например,
		/// требуется сделать преобразование int32(в БД) -> bool(свойство класса) 
		/// </summary>
		/// <param name="FieldName">Имя поля в БД, которое может быть использовано как признак необходимости преобразования</param>
		/// <param name="Value">Исходное значение, вычитанное из БД, которое может быть использовано для преобразования</param>
		/// <returns></returns>
		protected virtual object TryUserConversion(string FieldName, object Value)
		{
			return Value;
		}

		protected virtual object TryUserConversionForNull(string FieldName)
		{
			return null;
		}
	}
}