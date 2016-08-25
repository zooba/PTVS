using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Package;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Microsoft.PythonTools.Editor {
    class PythonDropdownBars : TypeAndMemberDropdownBars {
        private readonly ITextView _view;

        public PythonDropdownBars(LanguageService service, ITextView view) : base(service) {
            _view = view;
        }

        public override bool OnSynchronizeDropdowns(
            LanguageService languageService,
            IVsTextView textView,
            int line,
            int col,
            ArrayList dropDownTypes,
            ArrayList dropDownMembers,
            ref int selectedType,
            ref int selectedMember
        ) {
            throw new NotImplementedException();
        }
    }
}
