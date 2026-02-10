package com.mics.hooksdk.http;

import com.google.protobuf.Message;

import com.sun.net.httpserver.Headers;
import com.sun.net.httpserver.HttpExchange;

import java.io.IOException;
import java.io.OutputStream;

final class ProtobufHttp {
    private ProtobufHttp() {
    }

    static byte[] readBody(HttpExchange exchange) throws IOException {
        return exchange.getRequestBody().readAllBytes();
    }

    static void writeProtobuf(HttpExchange exchange, int statusCode, Message message) throws IOException {
        byte[] payload = message.toByteArray();
        Headers headers = exchange.getResponseHeaders();
        headers.set("Content-Type", "application/protobuf");
        exchange.sendResponseHeaders(statusCode, payload.length);
        try (OutputStream os = exchange.getResponseBody()) {
            os.write(payload);
        }
    }

    static void writeText(HttpExchange exchange, int statusCode, String text) throws IOException {
        byte[] payload = (text == null ? "" : text).getBytes(java.nio.charset.StandardCharsets.UTF_8);
        Headers headers = exchange.getResponseHeaders();
        headers.set("Content-Type", "text/plain; charset=utf-8");
        exchange.sendResponseHeaders(statusCode, payload.length);
        try (OutputStream os = exchange.getResponseBody()) {
            os.write(payload);
        }
    }
}

