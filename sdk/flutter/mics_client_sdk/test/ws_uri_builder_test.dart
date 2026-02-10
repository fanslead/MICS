import 'package:mics_client_sdk/src/ws_uri_builder.dart';
import 'package:test/test.dart';

void main() {
  test('buildWsUri should append required query params', () {
    final u = buildWsUri('ws://localhost:8080/ws', 't1', 'tok', 'dev1');
    expect(u.queryParameters['tenantId'], 't1');
    expect(u.queryParameters['token'], 'tok');
    expect(u.queryParameters['deviceId'], 'dev1');
  });

  test('buildWsUri should preserve existing query params', () {
    final u = buildWsUri('ws://localhost:8080/ws?x=1', 't1', 'tok', 'dev1');
    expect(u.queryParameters['x'], '1');
    expect(u.queryParameters['tenantId'], 't1');
  });
}

