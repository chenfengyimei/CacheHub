mod config;

use config::parser::ConfigParser;
use config::error::ConfigError;

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.len() < 2 {
        eprintln!("Usage: {} <config-file>", args[0]);
        std::process::exit(1);
    }

    match ConfigParser::parse_file(std::path::Path::new(&args[1])) {
        Ok(cfg) => {
            println!("Port: {}", cfg.get_or_default("port", "8080"));
            println!("Host: {}", cfg.get_or_default("host", "localhost"));
            if let Ok(Some(workers)) = cfg.get_as_int("workers") {
                println!("Workers: {}", workers);
            }
        }
        Err(e) => {
            eprintln!("Error: {}", e);
            std::process::exit(1);
        }
    }
}
