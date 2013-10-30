using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NRefactory.RenameClass
{
    public class CSharpFileEqualityComparer : IEqualityComparer<CSharpFile>
    {
        public bool Equals(CSharpFile x, CSharpFile y)
        {
            return x.OriginalText == y.OriginalText;
        }

        public int GetHashCode(CSharpFile obj)
        {
            return obj.OriginalText.GetHashCode();
        }
    }
}
