mergeInto(LibraryManager.library, {
  $TDSoloAPI: { pending: {} },
  TD_SoloRequest__deps: ['$TDSoloAPI'],
  TD_SoloRequest: function(id, methodPtr, urlPtr, bodyPtr, targetPtr) {
    var method = UTF8ToString(methodPtr), url = UTF8ToString(urlPtr);
    var body = UTF8ToString(bodyPtr), target = UTF8ToString(targetPtr);
    var request = new AbortController();
    TDSoloAPI.pending[id] = request;
    var options = {method: method, credentials: 'same-origin', signal: request.signal};
    if (body) { options.body = body; options.headers = {'Content-Type': 'application/json'}; }
    fetch(url, options).then(function(response) {
      return response.text().then(function(text) { return {id: id, status: response.status, body: text, error: ''}; });
    }).catch(function(error) {
      return {id: id, status: 0, body: '', error: String(error)};
    }).then(function(reply) {
      if (TDSoloAPI.pending[id] !== request) return;
      delete TDSoloAPI.pending[id];
      SendMessage(target, 'OnSoloResponse', JSON.stringify(reply));
    });
  },
  TD_SoloCancel__deps: ['$TDSoloAPI'],
  TD_SoloCancel: function(id) {
    var request = TDSoloAPI.pending[id]; delete TDSoloAPI.pending[id];
    if (request) request.abort();
  },
  TD_SoloCancelAll__deps: ['$TDSoloAPI'],
  TD_SoloCancelAll: function() {
    Object.keys(TDSoloAPI.pending).forEach(function(id) {
      var request = TDSoloAPI.pending[id]; delete TDSoloAPI.pending[id]; request.abort();
    });
  }
});
