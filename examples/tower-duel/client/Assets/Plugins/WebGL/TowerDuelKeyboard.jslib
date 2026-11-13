mergeInto(LibraryManager.library, {
  TD_CloseKeyboard__deps: ['JS_MobileKeyboard_Hide'],
  TD_CloseKeyboard: function () {
    // Use the pinned Unity keyboard lifecycle so its text/status are committed.
    if (typeof mobile_input !== 'undefined' && mobile_input) _JS_MobileKeyboard_Hide(0);
  }
});
