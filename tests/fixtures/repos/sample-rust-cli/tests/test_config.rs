use sample_rust_cli::config::parser::ConfigParser;
use sample_rust_cli::config::error::ConfigError;

#[test]
fn test_parse_valid_config() {
    let content = r#"
# Server config
port = 3000
host = 0.0.0.0
workers = 4
debug = true
"#;
    let cfg = ConfigParser::parse_str(content).unwrap();
    assert_eq!(cfg.get("port"), Some("3000"));
    assert_eq!(cfg.get("host"), Some("0.0.0.0"));
    assert_eq!(cfg.get_or_default("missing", "default"), "default");
}

#[test]
fn test_parse_int() {
    let content = "port = 8080\nworkers = 8";
    let cfg = ConfigParser::parse_str(content).unwrap();
    assert_eq!(cfg.get_as_int("workers").unwrap(), Some(8));
    assert_eq!(cfg.get_as_int("port").unwrap(), Some(8080));
}

#[test]
fn test_parse_invalid_int() {
    let content = "port = not_a_number";
    let cfg = ConfigParser::parse_str(content).unwrap();
    assert!(cfg.get_as_int("port").is_err());
}

#[test]
fn test_parse_empty_lines_and_comments() {
    let content = r#"

# This is a comment
port = 9090

# Another comment
debug = false
"#;
    let cfg = ConfigParser::parse_str(content).unwrap();
    assert_eq!(cfg.get("port"), Some("9090"));
    assert_eq!(cfg.get("debug"), Some("false"));
}

#[test]
fn test_parse_invalid_syntax() {
    let content = "this is not valid";
    let result = ConfigParser::parse_str(content);
    assert!(result.is_err());
}
