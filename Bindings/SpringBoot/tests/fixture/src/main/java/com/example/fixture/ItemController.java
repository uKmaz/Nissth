package com.example.fixture;

import java.util.List;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/items")
public class ItemController {

    private final ItemRepository repository;

    public ItemController(ItemRepository repository) {
        this.repository = repository;
    }

    @GetMapping
    public List<Item> list(@RequestParam(name = "q", required = false) String q) {
        if (q == null || q.isBlank()) return repository.findAll();
        return repository.findByNameContainingIgnoreCase(q);
    }
}
