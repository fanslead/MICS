# MICS Java SDKs

当前包含：
- `mics-hook-sdk`：服务端 Hook SDK（HTTP Hook + Kafka MQ Hook 事件解码/验签）

## Build / Test

需要 JDK 17+ 与 Maven：

```bash
cd sdk/java
mvn test
```

## Samples

```bash
cd sdk/java
mvn -pl samples/hook-server -am exec:java
```

Kafka 消费示例：

```bash
cd sdk/java
export TENANT_ID=t1
export TENANT_SECRET=secret
export KAFKA_BROKERS=localhost:9092
mvn -pl samples/kafka-consumer -am exec:java
```

Spring Boot Hook Server 示例：

```bash
cd sdk/java
mvn -pl samples/spring-hook-server -am spring-boot:run
```

