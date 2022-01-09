using System.Collections.Generic;

namespace Kari.Utils
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

        public bool Contains<T>() where T : BaseType
        {
            return _cache.ContainsKey(typeof(T));
        }

        // Creates and caches the resource, if it does not already exist
        public void Load<T>(System.Func<T> resourceCreator) where T : BaseType
        {
            if (!_cache.ContainsKey(typeof(T)))
            {
                var resource = resourceCreator();
                _cache[typeof(T)] = resource;
            }
        }

        public Dictionary<System.Type, BaseType>.ValueCollection Items => _cache.Values;
        public Dictionary<System.Type, BaseType> Raw => _cache;
    }
}