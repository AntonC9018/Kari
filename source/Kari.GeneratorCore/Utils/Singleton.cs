namespace Kari.GeneratorCore
{
    public class Singleton<T> where T : Singleton<T>
    {
        public static T Instance { get; private set; }
        public static void InitializeSingleton(T instance)
        {
            if (!(Instance is null)) throw new System.Exception("Cannot initialize a singleton multiple times.");
            Instance = instance;
        }
    }
}