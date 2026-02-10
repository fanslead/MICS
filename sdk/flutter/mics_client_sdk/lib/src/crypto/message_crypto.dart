import 'dart:typed_data';

abstract class MessageCrypto {
  Future<Uint8List> encrypt(Uint8List plaintext);
  Future<Uint8List> decrypt(Uint8List ciphertext);
}

