namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed partial class RuntimeMemorySummaryWindow : ScrollableDebuggerWindowBase
        {
            private sealed class Record
            {
                private readonly string _name;
                private int _count;
                private long _size;

                public Record(string name)
                {
                    _name = name;
                    _count = 0;
                    _size = 0L;
                }

                public string Name
                {
                    get
                    {
                        return _name;
                    }
                }

                public int Count
                {
                    get
                    {
                        return _count;
                    }
                    set
                    {
                        _count = value;
                    }
                }

                public long Size
                {
                    get
                    {
                        return _size;
                    }
                    set
                    {
                        _size = value;
                    }
                }
            }
        }
    }
}
