FROM rabbitmq:3-management

ADD docker/rabbitmq/rabbitmq.config /etc/rabbitmq/
ADD docker/rabbitmq/definitions.json /etc/rabbitmq/
HEALTHCHECK --interval=20s --timeout=5s --start-period=30s \
CMD rabbitmqctl status || exit 1
