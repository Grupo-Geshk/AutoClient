﻿namespace AutoClient.DTOs.Clients;

public class ClientDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Phone { get; set; }
    public string DNI { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
}
