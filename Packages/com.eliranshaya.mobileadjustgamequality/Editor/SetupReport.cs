using System.Collections.Generic;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>Collects what a Setup run did so the window can show it afterwards.</summary>
    internal class SetupReport
    {
        readonly List<string> m_Created = new List<string>();
        readonly List<string> m_Updated = new List<string>();
        readonly List<string> m_Notes = new List<string>();
        readonly List<string> m_Problems = new List<string>();

        /// <summary>Assets and settings created by this run.</summary>
        internal IReadOnlyList<string> Created => m_Created;

        /// <summary>Assets and settings that already existed and were rewritten.</summary>
        internal IReadOnlyList<string> Updated => m_Updated;

        /// <summary>Informational remarks worth showing the user.</summary>
        internal IReadOnlyList<string> Notes => m_Notes;

        /// <summary>Things that went wrong but did not abort the run.</summary>
        internal IReadOnlyList<string> Problems => m_Problems;

        /// <summary>True when at least one problem was recorded.</summary>
        internal bool HasProblems => m_Problems.Count > 0;

        /// <summary>Records a newly created asset or setting.</summary>
        /// <param name="what">Description, usually an asset path.</param>
        internal void Create(string what) => m_Created.Add(what);

        /// <summary>Records an existing asset or setting that was rewritten.</summary>
        /// <param name="what">Description, usually an asset path.</param>
        internal void Update(string what) => m_Updated.Add(what);

        /// <summary>Records an informational remark.</summary>
        /// <param name="what">The remark.</param>
        internal void Note(string what) => m_Notes.Add(what);

        /// <summary>Records a non-fatal failure.</summary>
        /// <param name="what">What failed and why.</param>
        internal void Problem(string what) => m_Problems.Add(what);
    }
}
