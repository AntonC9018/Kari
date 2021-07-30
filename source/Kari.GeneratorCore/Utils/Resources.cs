using System.Collections.Generic;

namespace Kari.GeneratorCore
{
    public readonly struct Resources<BaseType> where BaseType : class
    {
        private readonly Dictionary<System.Type, BaseType> _cache;

        public Resources(int size)
        {
            _cache = new Dictionary<System.Type, BaseType>(size);
        }

        public void Add<T>(T resource) where T : BaseType
        {
            _cache.Add(typeof(T), resource);
        }

        public T Get<T>() where T : BaseType
        {
            return (T) _cache[typeof(T)];
        }

        public Dictionary<System.Type, BaseType>.ValueCollection Items => _cache.Values;
        public Dictionary<System.Type, BaseType> Raw => _cache;
    }
}