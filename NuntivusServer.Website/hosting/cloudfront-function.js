function handler(event) {
  var request = event.request;
  var uri = request.uri;
  var host = request.headers && request.headers.host && request.headers.host.value;
  var pageWithoutSlash = !uri.endsWith('/') && !uri.substring(uri.lastIndexOf('/') + 1).includes('.');
  if (host === 'nuntivus.com' || pageWithoutSlash) {
    var query = [];
    var parameters = request.querystring || {};
    for (var key in parameters) {
      var parameter = parameters[key];
      var values = parameter.multiValue || [parameter];
      for (var i = 0; i < values.length; i++) {
        query.push(key + '=' + values[i].value);
      }
    }
    return {
      statusCode: 301,
      statusDescription: 'Moved Permanently',
      headers: {
        location: { value: 'https://www.nuntivus.com' + uri + (pageWithoutSlash ? '/' : '') + (query.length ? '?' + query.join('&') : '') }
      }
    };
  }
  if (uri.endsWith('/')) {
    request.uri += 'index.html';
  }
  return request;
}
