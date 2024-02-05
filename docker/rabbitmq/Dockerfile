FROM rabbitmq:3.12.12-management

COPY docker/rabbitmq/rabbitmq.config /etc/rabbitmq/
COPY docker/rabbitmq/definitions.json /etc/rabbitmq/
HEALTHCHECK --interval=20s --timeout=5s --start-period=30s \
CMD rabbitmqctl status || exit 1
