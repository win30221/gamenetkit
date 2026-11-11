mergeInto(LibraryManager.library, {
  PB_Report: function(message) {
    var value = UTF8ToString(message);
    globalThis.protobufPlayerResult = value;
    if (globalThis.document) document.documentElement.setAttribute('data-protobuf-result', value);
  }
});
