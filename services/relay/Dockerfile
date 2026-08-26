FROM golang:1.26.5-alpine AS build

ARG GOPROXY=https://proxy.golang.org,direct
ENV GOPROXY=${GOPROXY}
WORKDIR /src
COPY services/relay/go.mod services/relay/go.sum ./
RUN go mod download
COPY services/relay/ ./
RUN CGO_ENABLED=0 GOOS=linux go build -trimpath -ldflags="-s -w" -o /out/relay ./cmd/server

FROM alpine:3.22
RUN addgroup -S companion \
    && adduser -S -G companion companion
COPY --from=build /out/relay /usr/local/bin/relay
USER companion
EXPOSE 8080
ENTRYPOINT ["/usr/local/bin/relay"]
