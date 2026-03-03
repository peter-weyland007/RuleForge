window.markdownEditor = {
  insertAtCursor: function (hostId, snippet, fallbackValue) {
    const host = document.getElementById(hostId);
    if (!host) return (fallbackValue || '') + snippet;
    const el = host.querySelector('textarea, input');
    if (!el) return (fallbackValue || '') + snippet;
    const value = el.value || '';
    const start = typeof el.selectionStart === 'number' ? el.selectionStart : value.length;
    const end = typeof el.selectionEnd === 'number' ? el.selectionEnd : value.length;
    const next = value.slice(0, start) + snippet + value.slice(end);
    el.value = next;
    const pos = start + snippet.length;
    if (el.setSelectionRange) {
      el.focus();
      el.setSelectionRange(pos, pos);
    }
    return next;
  }
};
