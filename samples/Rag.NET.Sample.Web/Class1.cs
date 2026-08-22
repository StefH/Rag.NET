using ZeroAlloc.Cache;
using System;
using System.Collections.Generic;
using System.Text;

namespace Rag.NET.Sample.Web;

//[Cache(TtlMs = 60_000)]
public interface IClass1
{
    [Cache(TtlMs = 10000)]
    int X();

    [Cache(TtlMs = 10000)]
    Task<string> Y();
}


public class Class1 : IClass1
{
    public int X()
    {
        return 42;
    }
    
    public async Task<string> Y()
    {
        await Task.Delay(100);
        return "100";
    }
}