# Generating

## **Do not use these certificates in a production environment!!!**
Be careful when using self signed certificates, they are less secure
and should not be used in a production environment.

## CA Certificate

First generate a Certification Authority (CA) certificate for peer verification.

```powershell
# generate a private key
openssl genrsa 4096 > ca-key.pem

# generate the ca certificate
openssl req -new -x509 -nodes -days 365000 -key ca-key.pem -out ca-cert.pem
```

## Server certificate

Then generate a server certificate and sign it with the CA certificate.

```powershell
# generate a private key for the server
openssl req -newkey rsa:4096 -nodes -days 365000 -keyout server-key.pem -out server-req.pem

# generate the server certificate
openssl x509 -req -days 365000 -set_serial 01 -in server-req.pem -out server-cert.pem -CA ca-cert.pem -CAkey ca-key.pem
```

## Client certificate

When using client/server authentication, generate a client certificate and sign it with the same CA.

```powershell
# generate a private key for the client
openssl req -newkey rsa:4096 -nodes -days 365000 -keyout client-key.pem -out client-req.pem

# generate the client certificate
openssl x509 -req -days 365000 -set_serial 01 -in client-req.pem -out client-cert.pem -CA ca-cert.pem -CAkey ca-key.pem
```

### Generate pfx file

```powershell
openssl pkcs12 -inkey client-key.pem -in client-cert.pem -export -out client-cert.pfx
```

The passsword for the pfx file in this repository is `RabbitMQClient`.

