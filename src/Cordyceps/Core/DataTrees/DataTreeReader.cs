using System.Collections.Generic;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Cordyceps.Core.DataTrees
{
    /// <summary>
    /// Host adapter from a Grasshopper data tree to the host-free <see cref="TreeBranch"/> list, so
    /// every reader of tree shape counts nulls and invalid items the same way. Not linked into the
    /// test project (it needs Grasshopper types).
    /// </summary>
    public static class DataTreeReader
    {
        public static List<TreeBranch> ReadBranches(IGH_Structure data)
        {
            var branches = new List<TreeBranch>(data.PathCount);
            for (int b = 0; b < data.PathCount; b++)
            {
                var list = data.get_Branch(b);
                int nulls = 0, invalid = 0;
                foreach (var item in list)
                {
                    if (item == null) nulls++;
                    else if (item is IGH_Goo goo && !goo.IsValid) invalid++;
                }
                branches.Add(new TreeBranch(data.get_Path(b).Indices, list.Count, nulls, invalid));
            }
            return branches;
        }
    }
}
