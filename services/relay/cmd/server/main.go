package main

import (
	"context"
	"errors"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/local/codex-companion/relay/internal/pairing"
	"github.com/local/codex-companion/relay/internal/routing"
	"github.com/local/codex-companion/relay/internal/storage"
	websocketserver "github.com/local/codex-companion/relay/internal/websocket"
)

func main() {
	logger := slog.New(slog.NewJSONHandler(os.Stdout, &slog.HandlerOptions{Level: parseLogLevel(os.Getenv("LOG_LEVEL"))}))
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	store, err := openStore(ctx, logger)
	if err != nil {
		logger.Error("failed to initialize storage", "error", err)
		os.Exit(1)
	}
	defer store.Close()

	hub := routing.NewHub()
	pairingService := pairing.NewService(store, 10*time.Minute)
	wsServer := websocketserver.New(hub, pairingService, originPatterns(), logger)

	router := chi.NewRouter()
	router.Get("/healthz", func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"status":"ok"}`))
	})
	router.Get("/ws/bridge", wsServer.Bridge)
	router.Get("/ws/web", wsServer.Web)

	server := &http.Server{
		Addr:              envOr("RELAY_ADDR", ":8080"),
		Handler:           router,
		ReadHeaderTimeout: 5 * time.Second,
		IdleTimeout:       60 * time.Second,
	}
	go func() {
		logger.Info("relay listening", "address", server.Addr)
		if err := server.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			logger.Error("relay stopped unexpectedly", "error", err)
			stop()
		}
	}()

	<-ctx.Done()
	shutdownContext, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := server.Shutdown(shutdownContext); err != nil {
		logger.Error("relay shutdown failed", "error", err)
	}
}

func openStore(ctx context.Context, logger *slog.Logger) (storage.Store, error) {
	databaseURL := os.Getenv("DATABASE_URL")
	if databaseURL == "" {
		logger.Warn("DATABASE_URL is unset; using non-persistent in-memory pairing store")
		return storage.NewMemoryStore(), nil
	}
	return storage.NewPostgresStore(ctx, databaseURL)
}

func originPatterns() []string {
	raw := envOr("ALLOWED_ORIGINS", "localhost:*,127.0.0.1:*,192.168.*:*,10.*:*,172.*:*")
	patterns := make([]string, 0)
	for _, item := range strings.Split(raw, ",") {
		if value := strings.TrimSpace(item); value != "" {
			patterns = append(patterns, value)
		}
	}
	return patterns
}

func envOr(name, fallback string) string {
	if value := strings.TrimSpace(os.Getenv(name)); value != "" {
		return value
	}
	return fallback
}

func parseLogLevel(value string) slog.Level {
	switch strings.ToLower(strings.TrimSpace(value)) {
	case "debug":
		return slog.LevelDebug
	case "warn", "warning":
		return slog.LevelWarn
	case "error":
		return slog.LevelError
	default:
		return slog.LevelInfo
	}
}
