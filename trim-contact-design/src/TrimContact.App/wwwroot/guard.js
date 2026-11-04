window.trimGuard = (dirty) => { window.onbeforeunload = dirty ? (e) => {e.preventDefault(); e.returnValue = '';} : null; };
