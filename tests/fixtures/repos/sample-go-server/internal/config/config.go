package config

type Config struct {
	Port    string
	Workers int
}

func Default() Config {
	return Config{
		Port:    ":8080",
		Workers: 4,
	}
}
