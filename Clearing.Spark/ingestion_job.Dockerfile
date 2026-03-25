# Re-applying the working Apache Spark image for reliable metadata resolution
FROM apache/spark:3.5.0-python3

USER root

# Install dependencies for building descriptors and downloading jars
RUN apt-get update && apt-get install -y curl protobuf-compiler && rm -rf /var/lib/apt/lists/*

# --- DOWNLOAD JARS ---
ENV JARS_DIR=/opt/spark/jars
RUN curl -L -o ${JARS_DIR}/postgresql-42.7.3.jar https://repo1.maven.org/maven2/org/postgresql/postgresql/42.7.3/postgresql-42.7.3.jar
RUN curl -L -o ${JARS_DIR}/iceberg-spark-runtime-3.5_2.12-1.5.2.jar https://repo1.maven.org/maven2/org/apache/iceberg/iceberg-spark-runtime-3.5_2.12/1.5.2/iceberg-spark-runtime-3.5_2.12-1.5.2.jar
RUN curl -L -o ${JARS_DIR}/awssdk-bundle-2.21.1.jar https://repo1.maven.org/maven2/software/amazon/awssdk/bundle/2.21.1/bundle-2.21.1.jar
RUN curl -L -o ${JARS_DIR}/url-connection-client-2.21.1.jar https://repo1.maven.org/maven2/software/amazon/awssdk/url-connection-client/2.21.1/url-connection-client-2.21.1.jar
RUN curl -L -o ${JARS_DIR}/hadoop-aws-3.3.4.jar https://repo1.maven.org/maven2/org/apache/hadoop/hadoop-aws/3.3.4/hadoop-aws-3.3.4.jar
RUN curl -L -o ${JARS_DIR}/aws-java-sdk-bundle-1.12.262.jar https://repo1.maven.org/maven2/com/amazonaws/aws-java-sdk-bundle/1.12.262/aws-java-sdk-bundle-1.12.262.jar
RUN curl -L -o ${JARS_DIR}/spark-sql-kafka-0-10_2.12-3.5.0.jar https://repo1.maven.org/maven2/org/apache/spark/spark-sql-kafka-0-10_2.12/3.5.0/spark-sql-kafka-0-10_2.12-3.5.0.jar
RUN curl -L -o ${JARS_DIR}/spark-protobuf_2.12-3.5.0.jar https://repo1.maven.org/maven2/org/apache/spark/spark-protobuf_2.12/3.5.0/spark-protobuf_2.12-3.5.0.jar
RUN curl -L -o ${JARS_DIR}/kafka-clients-3.4.1.jar https://repo1.maven.org/maven2/org/apache/kafka/kafka-clients/3.4.1/kafka-clients-3.4.1.jar
RUN curl -L -o ${JARS_DIR}/commons-pool2-2.11.1.jar https://repo1.maven.org/maven2/org/apache/commons/commons-pool2/2.11.1/commons-pool2-2.11.1.jar
RUN curl -L -o ${JARS_DIR}/spark-token-provider-kafka-0-10_2.12-3.5.0.jar https://repo1.maven.org/maven2/org/apache/spark/spark-token-provider-kafka-0-10_2.12/3.5.0/spark-token-provider-kafka-0-10_2.12-3.5.0.jar

# --- PREPARE SCHEMAS ---
WORKDIR /app
COPY Messaging.Kafka/Messaging.Contracts/ProtoSchemas/ ./ProtoSchemas/

# Create Binary Descriptor
RUN protoc -I=./ProtoSchemas --include_imports --include_source_info --descriptor_set_out=journal.desc ./ProtoSchemas/ledger_journal_entry.proto

# --- FINALIZE ---
COPY Clearing.Spark/ingestion_job.py .

# Apache Spark user id 185
USER 185

CMD ["/opt/spark/bin/spark-submit", \
     "--master", "local[*]", \
     "ingestion_job.py"]
