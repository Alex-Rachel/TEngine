namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed partial class RuntimeMemoryInformationWindow<T> : ScrollableDebuggerWindowBase where T : UnityEngine.Object
        {
            private sealed class Sample
            {
                private readonly string _name;
                private readonly string _type;
                private readonly long _size;
                private bool _highlight;

                public Sample(string name, string type, long size)
                {
                    _name = name;
                    _type = type;
                    _size = size;
                    _highlight = false;
                }

                public string Name
                {
                    get
                    {
                        return _name;
                    }
                }

                public string Type
                {
                    get
                    {
                        return _type;
                    }
                }

                public long Size
                {
                    get
                    {
                        return _size;
                    }
                }

                public bool Highlight
                {
                    get
                    {
                        return _highlight;
                    }
                    set
                    {
                        _highlight = value;
                    }
                }
            }
        }
    }
}
