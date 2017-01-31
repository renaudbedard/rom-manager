using System;

namespace RomManager
{
    public static class SqliteHelper
    {
        public static T GetNullable<T>(object value) where T : class
        {
            return value is DBNull ? null : (T)value;
        }
        public static T? GetNullableValue<T>(object value) where T : struct
        {
            return value is DBNull ? null : (T?)(T)value;
        }
    }
}
