import 'dart:typed_data';

import 'package:cryptography/cryptography.dart';

import 'message_crypto.dart';

class AesGcmMessageCrypto implements MessageCrypto {
  static const int _version = 1;
  static const int _nonceBytes = 12;
  static const int _tagBytes = 16;

  final SecretKey _key;
  final AesGcm _aes;

  AesGcmMessageCrypto(Uint8List keyBytes)
      : _key = SecretKey(keyBytes),
        _aes = AesGcm.with256bits();

  @override
  Future<Uint8List> encrypt(Uint8List plaintext) async {
    if (plaintext.isEmpty) return Uint8List(0);
    final nonce = _aes.newNonce();
    final secretBox = await _aes.encrypt(plaintext, secretKey: _key, nonce: nonce);

    final out = BytesBuilder(copy: false);
    out.add([_version]);
    out.add(nonce);
    out.add(secretBox.mac.bytes);
    out.add(secretBox.cipherText);
    return out.toBytes();
  }

  @override
  Future<Uint8List> decrypt(Uint8List ciphertext) async {
    if (ciphertext.isEmpty) return Uint8List(0);
    if (ciphertext.length < 1 + _nonceBytes + _tagBytes) {
      throw ArgumentError('ciphertext too short');
    }

    final version = ciphertext[0];
    if (version != _version) {
      throw ArgumentError('unsupported ciphertext version');
    }

    final nonce = ciphertext.sublist(1, 1 + _nonceBytes);
    final macBytes = ciphertext.sublist(1 + _nonceBytes, 1 + _nonceBytes + _tagBytes);
    final cipherText = ciphertext.sublist(1 + _nonceBytes + _tagBytes);

    final box = SecretBox(cipherText, nonce: nonce, mac: Mac(macBytes));
    final plain = await _aes.decrypt(box, secretKey: _key);
    return Uint8List.fromList(plain);
  }
}
