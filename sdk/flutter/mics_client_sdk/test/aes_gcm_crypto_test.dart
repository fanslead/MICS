import 'dart:typed_data';

import 'package:mics_client_sdk/mics_client_sdk.dart';
import 'package:test/test.dart';

void main() {
  test('AesGcmMessageCrypto encrypt/decrypt roundtrip', () async {
    final crypto = AesGcmMessageCrypto(Uint8List(32));
    final plain = Uint8List.fromList(List<int>.generate(128, (i) => i & 0xff));
    final enc = await crypto.encrypt(plain);
    expect(enc, isNot(equals(plain)));
    final dec = await crypto.decrypt(enc);
    expect(dec, equals(plain));
  });
}

